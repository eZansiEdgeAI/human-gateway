using System.Collections.Concurrent;
using HumanGateway.Core.Artifacts;
using HumanGateway.Core.Hashing;
using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Api;
using HumanGateway.Relay.Options;
using HumanGateway.Relay.Storage;
using Microsoft.Extensions.Options;

namespace HumanGateway.Relay.Services;

/// <summary>
/// The Relay's artifact byte-channel service (RELAY-FR-01, ARTF-FR-01/02/03): serves the dedup state check,
/// the offset-addressed resumable upload, the hash-verified completion, and the streaming download. Every
/// operation requires a REGISTERED gateway (SP-02). Bytes land in the content-addressed BYTEA store
/// (<see cref="PostgresArtifactStore"/>) which deduplicates identical content and verifies hashes on write.
///
/// <para><b>Resumable uploads.</b> Chunks are accepted idempotently by offset into an in-memory partial buffer
/// (single Relay instance in v1, NF-10). An interrupted transfer resumes from <see cref="GetOffsetAsync"/> —
/// the durably accepted byte count. On <see cref="CompleteAsync"/> the accumulated bytes are verified against
/// the declared content hash before being published (SP-06), so a truncated or corrupt transfer can never
/// reach the store. A Relay restart loses in-memory partials; the sender transparently re-uploads from zero
/// (correct, merely less efficient).</para>
///
/// <para><b>Limits and quota (ARTF-FR-03).</b> A single artifact may not exceed <see cref="RelayArtifactOptions.MaxArtifactSizeBytes"/>
/// and the aggregate BYTEA budget may not exceed <see cref="RelayArtifactOptions.QuotaBytes"/> (deduplicated
/// content counts once, matching the bytes actually stored).</para>
/// </summary>
public sealed class RelayArtifactService
{
    /// <summary>In-memory partial upload state per canonical content hash. Static: survives across the scoped service instances.</summary>
    private static readonly ConcurrentDictionary<string, PartialUpload> Partials = new();

    private readonly GatewayService _gatewayService;
    private readonly PostgresArtifactStore _store;
    private readonly RelayArtifactOptions _options;
    private readonly ILogger<RelayArtifactService> _logger;

    public RelayArtifactService(
        GatewayService gatewayService,
        PostgresArtifactStore store,
        IOptions<RelayOptions> options,
        ILogger<RelayArtifactService> logger)
    {
        _gatewayService = gatewayService;
        _store = store;
        _options = options.Value.Artifacts;
        _logger = logger;
    }

    // -----------------------------------------------------------------------------------------------
    // Dedup state (ARTF-FR-01, NF-03)
    // -----------------------------------------------------------------------------------------------

    /// <summary>Returns the subset of <paramref name="hashes"/> the Relay already holds (skip transfer for these).</summary>
    public async Task<IReadOnlyList<string>> CheckHashesAsync(
        string gatewayId, IReadOnlyCollection<string> hashes, CancellationToken ct)
    {
        await _gatewayService.RequireRegisteredAsync(gatewayId, ct);

        var present = new List<string>();
        foreach (var hash in hashes.Distinct(StringComparer.Ordinal))
        {
            if (await _store.ExistsAsync(hash, ct).ConfigureAwait(false))
            {
                present.Add(hash);
            }
        }

        return present;
    }

    // -----------------------------------------------------------------------------------------------
    // Resumable upload (ARTF-FR-02)
    // -----------------------------------------------------------------------------------------------

    /// <summary>How many bytes the Relay durably holds for a hash, and whether the content is complete.</summary>
    public async Task<ArtifactOffsetState> GetOffsetAsync(string gatewayId, string hash, CancellationToken ct)
    {
        await _gatewayService.RequireRegisteredAsync(gatewayId, ct);

        var stored = await _store.GetSizeAsync(hash, ct).ConfigureAwait(false);
        if (stored is not null)
        {
            return new ArtifactOffsetState { Received = stored.Value, Complete = true };
        }

        var key = CanonicalKey(hash);
        Partials.TryGetValue(key, out var partial);
        return new ArtifactOffsetState { Received = partial?.Length ?? 0, Complete = false };
    }

    /// <summary>
    /// Accepts one chunk that must land at byte <paramref name="offset"/>. Idempotent per (hash, offset):
    /// a replayed chunk appends nothing, and a chunk that does not match the expected offset is rejected with
    /// a conflict so the sender re-queries the authoritative offset (resume).
    /// </summary>
    public async Task<ArtifactChunkResult> UploadChunkAsync(
        string gatewayId, string hash, long offset, Stream body, CancellationToken ct)
    {
        await _gatewayService.RequireRegisteredAsync(gatewayId, ct);
        ArgumentNullException.ThrowIfNull(body);
        if (offset < 0)
        {
            throw GatewayServiceException.BadRequest(ErrorCodes.BadRequest, "The chunk offset cannot be negative.");
        }

        var key = CanonicalKey(hash);

        // Dedup: identical content already published — nothing more to accept.
        var stored = await _store.GetSizeAsync(key, ct).ConfigureAwait(false);
        if (stored is not null)
        {
            return new ArtifactChunkResult { Received = stored.Value, Complete = true };
        }

        if (offset >= _options.MaxArtifactSizeBytes)
        {
            throw SizeExceeded(hash, offset, 0);
        }

        var partial = Partials.GetOrAdd(key, static _ => new PartialUpload());
        await partial.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (partial.Length != offset)
            {
                // Offset mismatch — report where the receiver actually is so the sender can resume (409).
                throw new GatewayServiceException(StatusCodes.Status409Conflict, ErrorCodes.Conflict,
                    $"Chunk at offset {offset} for '{hash}' does not match the received position {partial.Length}; re-query the offset.",
                    retryable: true);
            }

            await CopyBoundedAsync(partial, body, key, ct).ConfigureAwait(false);

            if (partial.Length > _options.MaxArtifactSizeBytes)
            {
                Partials.TryRemove(key, out _);
                throw SizeExceeded(hash, offset, partial.Length);
            }

            return new ArtifactChunkResult
            {
                Received = partial.Length,
                Complete = false,
            };
        }
        finally
        {
            partial.Gate.Release();
        }
    }

    /// <summary>
    /// Finalises an upload: verifies the accumulated bytes against the declared content hash (SP-06), enforces
    /// the blob-store quota (ARTF-FR-03), and publishes durably (deduplicating against a concurrent identical
    /// upload). A hash mismatch discards the partial and raises <see cref="ArtifactHashMismatchException"/>.
    /// </summary>
    public async Task<ArtifactCompleteResult> CompleteAsync(string gatewayId, string hash, CancellationToken ct)
    {
        await _gatewayService.RequireRegisteredAsync(gatewayId, ct);

        var key = CanonicalKey(hash);

        // Dedup — already complete, nothing to publish.
        var stored = await _store.GetSizeAsync(key, ct).ConfigureAwait(false);
        if (stored is not null)
        {
            return new ArtifactCompleteResult { Stored = false };
        }

        if (!Partials.TryRemove(key, out var partial))
        {
            throw new GatewayServiceException(StatusCodes.Status409Conflict, ErrorCodes.Conflict,
                $"No partial upload exists for '{hash}'; start the upload before completing it.",
                retryable: true);
        }

        try
        {
            // Quota accounting is content-addressed: only genuinely new bytes count (dedup, ARTF-FR-01).
            var used = await _store.GetTotalSizeBytesAsync(ct).ConfigureAwait(false);
            if (ArtifactLimits.ExceedsMaxSize(used + partial.Length, _options.QuotaBytes))
            {
                throw new GatewayServiceException(StatusCodes.Status413PayloadTooLarge, ErrorCodes.QuotaExceeded,
                    $"Publishing '{hash}' ({partial.Length} bytes) would use {used + partial.Length} of the Relay's "
                    + $"{_options.QuotaBytes} byte quota.");
            }

            // SaveAsync hashes the bytes while writing and rejects anything that does not match (SP-06).
            partial.Buffer.Position = 0;
            var storedNow = await _store.SaveAsync(partial.Buffer, key, ct).ConfigureAwait(false);
            _logger.LogInformation("Artifact {Hash} published ({SizeBytes} bytes, stored: {Stored})",
                key, partial.Length, storedNow);
            return new ArtifactCompleteResult { Stored = storedNow };
        }
        catch (ArtifactHashMismatchException)
        {
            _logger.LogWarning("Artifact upload of {Hash} failed content-hash verification; partial discarded", key);
            throw;
        }
    }

    // -----------------------------------------------------------------------------------------------
    // Download
    // -----------------------------------------------------------------------------------------------

    /// <summary>Opens the stored bytes for a hash as a streaming read, or null when the Relay does not hold it.</summary>
    public async Task<Stream?> DownloadAsync(string gatewayId, string hash, CancellationToken ct)
    {
        await _gatewayService.RequireRegisteredAsync(gatewayId, ct);
        return await _store.OpenReadAsync(hash, ct).ConfigureAwait(false);
    }

    /// <summary>The stored byte size of a hash, or null when the Relay does not hold it (Range download math).</summary>
    public async Task<long?> GetStoredSizeAsync(string gatewayId, string hash, CancellationToken ct)
    {
        await _gatewayService.RequireRegisteredAsync(gatewayId, ct);
        return await _store.GetSizeAsync(hash, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    /// <summary>Normalises a content hash to the canonical <c>sha256:&lt;lowercase hex&gt;</c> storage key.</summary>
    private static string CanonicalKey(string hash)
        => ContentHasher.AlgorithmPrefix + ArtifactHash.RequireHex(hash);

    /// <summary>413 — the artifact exceeds the configured per-artifact size limit (ARTF-FR-03).</summary>
    private static GatewayServiceException SizeExceeded(string hash, long offset, long currentLength)
        => new(StatusCodes.Status413PayloadTooLarge, ErrorCodes.SizeExceeded,
            $"Artifact '{hash}' would exceed the Relay's per-artifact size limit (chunk at offset {offset}, "
            + $"{currentLength} bytes buffered).");

    /// <summary>
    /// Appends the request body into a partial buffer. The copy is bounded by the configured max artifact
    /// size: once the buffer would exceed it, the partial is discarded and the upload rejected — a malicious
    /// or buggy sender cannot buffer unbounded bytes.
    /// </summary>
    private async Task CopyBoundedAsync(PartialUpload partial, Stream body, string key, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (partial.Length + read > _options.MaxArtifactSizeBytes)
            {
                Partials.TryRemove(key, out _);
                throw SizeExceeded(key, partial.Length, partial.Length);
            }

            await partial.Buffer.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Per-hash partial-upload state: a gate (for serialised chunk acceptance) and the accumulated bytes.</summary>
    private sealed class PartialUpload
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public MemoryStream Buffer { get; } = new();

        public long Length => Buffer.Length;
    }
}
