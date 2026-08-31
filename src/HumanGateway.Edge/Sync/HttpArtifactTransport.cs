using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HumanGateway.Core.Artifacts;
using HumanGateway.Core.Hashing;
using HumanGateway.Edge.Api;
using HumanGateway.Protocol.Models;
using Microsoft.Extensions.Options;

namespace HumanGateway.Edge.Sync;

/// <summary>
/// The Edge's real outbound artifact-byte channel to the Relay (ARTF-FR-01, PROTO-FR-04 exception). Bytes
/// flow in resumable, content-addressed chunks (<see cref="ChunkedArtifactTransfer"/> over the Relay's
/// <c>/sync/artifacts</c> endpoints): the Edge first asks which hashes the Relay lacks (dedup — skip transfer
/// for known content, NF-03), uploads only the missing bytes with offset-based resume, and the Relay verifies
/// the content hash before publishing (SP-06). Downloads resume via Range reads into a local partial file.
///
/// <para>The channel is outbound-only (SP-01) and scoped to artifact bytes; the sync-batch transport is owned
/// by the synchronisation feature.</para>
/// </summary>
public sealed class HttpArtifactTransport : IArtifactTransfer, IChunkedArtifactChannel
{
    private static readonly JsonSerializerOptions WireJson = RelayWireJson();

    private readonly HttpClient _http;
    private readonly RelayArtifactOptions _options;
    private readonly string _gatewayId;
    private readonly ILogger<HttpArtifactTransport> _logger;

    /// <summary>Creates the transport over the given client, options, and the gateway identity it uploads as.</summary>
    public HttpArtifactTransport(
        HttpClient http,
        IOptions<RelayArtifactOptions> options,
        IOptions<GatewayOptions> gateway,
        ILogger<HttpArtifactTransport> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _gatewayId = gateway?.Value.GatewayId ?? throw new ArgumentNullException(nameof(gateway));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsConfigured => _options.Enabled;

    private int ChunkSize => Math.Max(1, _options.ChunkSizeBytes);

    // -----------------------------------------------------------------------------------------------
    // IArtifactTransfer
    // -----------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> CheckHashesAsync(IReadOnlyCollection<string> hashes, CancellationToken ct = default)
    {
        if (hashes is null || hashes.Count == 0)
        {
            return Array.Empty<string>();
        }

        var response = await _http.PostAsJsonAsync(
            "/sync/artifacts/state",
            new { gatewayId = _gatewayId, hashes = hashes.Distinct().ToList() },
            WireJson,
            ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "checking artifact presence", ct).ConfigureAwait(false);

        var state = await response.Content.ReadFromJsonAsync<ArtifactStateResponse>(WireJson, ct).ConfigureAwait(false);
        var present = state?.Present ?? Array.Empty<string>();
        var presentSet = new HashSet<string>(present, StringComparer.Ordinal);

        // Dedup (ARTF-FR-01, NF-03): transfer only what the Relay does not already hold.
        return hashes.Distinct().Where(h => !presentSet.Contains(h)).ToList();
    }

    /// <inheritdoc />
    public async Task UploadAsync(string hash, long sizeBytes, Stream content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        await ChunkedArtifactTransfer.UploadAsync(this, content, hash, ChunkSize, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long?> GetRemoteSizeAsync(string hash, CancellationToken ct = default)
    {
        var state = await FetchOffsetStateAsync(hash, ct).ConfigureAwait(false);
        return state is { Complete: true } ? state.Received : null;
    }

    /// <inheritdoc />
    public async Task<long> DownloadAsync(string hash, Stream sink, CancellationToken ct = default)
        => await ChunkedArtifactTransfer.DownloadAsync(this, sink, hash, ChunkSize, ct).ConfigureAwait(false);

    // -----------------------------------------------------------------------------------------------
    // IChunkedArtifactChannel — the resumable framing over the Relay endpoints
    // -----------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<long> GetResumeOffsetAsync(string hash, CancellationToken ct = default)
    {
        var state = await FetchOffsetStateAsync(hash, ct).ConfigureAwait(false);
        return state?.Received ?? 0;
    }

    /// <inheritdoc />
    public async Task SendChunkAsync(string hash, long offset, ReadOnlyMemory<byte> chunk, CancellationToken ct = default)
    {
        using var body = new ByteArrayContent(chunk.ToArray());
        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"/sync/artifacts/{Uri.EscapeDataString(hash)}?offset={offset}&gatewayId={Uri.EscapeDataString(_gatewayId)}")
        {
            Content = body,
        };

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // The receiver's partial state diverged — the driver re-queries the authoritative offset.
            throw new ChunkOffsetMismatchException(offset, hash);
        }

        await EnsureSuccessAsync(response, $"uploading chunk at {offset} for '{hash}'", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(string hash, CancellationToken ct = default)
    {
        using var response = await _http.PostAsync(
            $"/sync/artifacts/{Uri.EscapeDataString(hash)}/complete?gatewayId={Uri.EscapeDataString(_gatewayId)}", content: null, ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // A hash mismatch on completion surfaces the declared/actual pair for diagnosis (SP-06, SP-07).
        var error = await TryReadProtocolErrorAsync(response, ct).ConfigureAwait(false);
        if (error is { Code: ErrorCodes.HashMismatch } && TryReadHashPair(error, out var declared, out var actual))
        {
            throw new ArtifactHashMismatchException(declared, actual);
        }

        await EnsureSuccessAsync(response, $"completing upload of '{hash}'", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> GetSizeAsync(string hash, CancellationToken ct = default)
    {
        var state = await FetchOffsetStateAsync(hash, ct).ConfigureAwait(false);
        return state is { Complete: true } ? state.Received : -1;
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenRangeAsync(string hash, long offset, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/sync/artifacts/{Uri.EscapeDataString(hash)}?gatewayId={Uri.EscapeDataString(_gatewayId)}");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        await EnsureSuccessAsync(response, $"downloading '{hash}' from offset {offset}", ct).ConfigureAwait(false);
        return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    private async Task<OffsetState?> FetchOffsetStateAsync(string hash, CancellationToken ct)
    {
        using var response = await _http.GetAsync(
            $"/sync/artifacts/{Uri.EscapeDataString(hash)}/offset?gatewayId={Uri.EscapeDataString(_gatewayId)}", ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"querying upload offset for '{hash}'", ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<OffsetState>(WireJson, ct).ConfigureAwait(false);
    }

    /// <summary>Throws on a non-success response, mapping known Relay <see cref="ProtocolError"/> codes to typed exceptions.</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = await TryReadProtocolErrorAsync(response, ct).ConfigureAwait(false);
        var message = error is null
            ? $"Relay artifact transport failed while {action} (HTTP {(int)response.StatusCode})."
            : $"Relay artifact transport failed while {action}: {error.Message} (code {error.Code}).";

        throw error?.Code switch
        {
            ErrorCodes.ArtifactNotFound or ErrorCodes.NotFound => new ArtifactNotFoundException(message),
            _ => new HttpRequestException(message, null, response.StatusCode),
        };
    }

    private static async Task<ProtocolError?> TryReadProtocolErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ProtocolError>(WireJson, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Extracts the declared/actual hash pair from a HASH_MISMATCH error's structured details.</summary>
    private static bool TryReadHashPair(ProtocolError error, out string declared, out string actual)
    {
        declared = string.Empty;
        actual = string.Empty;
        if (error.Details is not { } details || details.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!details.TryGetProperty("declaredHash", out var declaredElement)
            || !details.TryGetProperty("actualHash", out var actualElement))
        {
            return false;
        }

        declared = declaredElement.GetString() ?? string.Empty;
        actual = actualElement.GetString() ?? string.Empty;
        return declared.Length > 0 && actual.Length > 0;
    }

    private static JsonSerializerOptions RelayWireJson()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        return options;
    }

    private sealed record ArtifactStateResponse
    {
        public IReadOnlyList<string>? Present { get; init; }
    }

    private sealed record OffsetState
    {
        public long Received { get; init; }
        public bool Complete { get; init; }
    }
}
