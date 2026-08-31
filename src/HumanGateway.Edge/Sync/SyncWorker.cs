using HumanGateway.Core.Artifacts;
using HumanGateway.Core.Cursor;
using HumanGateway.Core.Outbox;
using HumanGateway.Core.Sync;
using HumanGateway.Edge.Api;
using HumanGateway.Protocol.Models;
using Microsoft.Extensions.Options;

namespace HumanGateway.Edge.Sync;

/// <summary>
/// The Edge Gateway's background sync worker (EDGE-FR-05, product vision §6.2/§10): a hosted service that
/// periodically performs outbound-only sync with the Cloud Relay (SP-01). It drives the sync-engineer's
/// <see cref="ISyncEngine"/> — the engine owns cursor/idempotency/ordering decisions, the worker only
/// orchestrates transport and lifecycle:
///
/// <list type="number">
/// <item><b>Push</b> — build the next batch from the durable outbox via the engine, send it through
/// <see cref="IRelaySyncClient"/>, and mark the covered entries sent only after the Relay acknowledges
/// (write-then-ack, EDGE-FR-04).</item>
/// <item><b>Pull</b> — request inbound batches, apply them idempotently via the engine, and enqueue the
/// resulting delivery acknowledgements for the next push (SYNC-FR-05).</item>
/// </list>
///
/// The worker's cursor state is <em>durable</em> (SYNC-FR-03): the Relay-issued push cursor, the gateway's own
/// pull cursor, and the in-flight push batch identity are persisted through <see cref="ISyncCursorStore"/> so
/// a restart resumes from the last cursor (never full-state resync, NF-02) and a retried push reuses the same
/// <c>batchId</c> + <c>idempotencyKey</c> (SYNC-FR-02, NF-05). Transient failures back off with capped,
/// jittered exponential backoff (SYNC-FR-04); outbox entries are never dropped and are never marked FAILED
/// merely for being offline — they are retained and retried (WAITING_FOR_SYNC is a valid state, never an
/// error). Every create was committed to SQLite before this worker ever saw it, so a crash mid-cycle can never
/// lose or duplicate a message (EDGE-FR-07).
/// </summary>
public sealed class SyncWorker : BackgroundService
{
    private readonly ISyncEngine _engine;
    private readonly IOutbox _outbox;
    private readonly ISyncCursorStore _cursors;
    private readonly IRelaySyncClient _relay;
    private readonly IArtifactStore _artifactStore;
    private readonly IArtifactTransfer _artifactTransfer;
    private readonly IOptions<GatewayOptions> _gateway;
    private readonly IOptions<SyncWorkerOptions> _options;
    private readonly ILogger<SyncWorker> _logger;
    private readonly TimeProvider _time;

    // Durable cursor state, loaded once from ISyncCursorStore on reconcile/first cycle (SYNC-FR-03). Cursors
    // are opaque tokens: the worker stores and echoes them, never interprets them.
    private bool _stateLoaded;
    private string? _pushCursor; // Relay's ack cursor, echoed as sinceCursor on push.
    private string? _pullCursor; // Gateway's own cursor, echoed as sinceCursor on pull.

    // In-flight push batch identity, persisted durably before the network attempt so a retry (or a restart
    // after a crash mid-push / lost ack) reuses the same batchId + idempotencyKey and the Relay collapses it
    // as a replay (SYNC-FR-02, NF-05).
    private string? _inFlightBatchId;
    private string? _inFlightIdempotencyKey;
    private long? _inFlightAfterSequence;

    /// <summary>Creates the worker over the sync engine, durable outbox, durable cursor store, transport hook, and options.</summary>
    public SyncWorker(
        ISyncEngine engine,
        IOutbox outbox,
        ISyncCursorStore cursors,
        IRelaySyncClient relay,
        IArtifactStore artifactStore,
        IArtifactTransfer artifactTransfer,
        IOptions<GatewayOptions> gateway,
        IOptions<SyncWorkerOptions> options,
        ILogger<SyncWorker> logger,
        TimeProvider? time = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _cursors = cursors ?? throw new ArgumentNullException(nameof(cursors));
        _relay = relay ?? throw new ArgumentNullException(nameof(relay));
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _artifactTransfer = artifactTransfer ?? throw new ArgumentNullException(nameof(artifactTransfer));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The worker's current lifecycle state (product vision §10), surfaced for observability (NF-09).</summary>
    public SyncWorkerState State { get; private set; } = SyncWorkerState.Starting;

    /// <summary>UTC time of the last completed sync cycle, or null before the first.</summary>
    public DateTimeOffset? LastSyncAtUtc { get; private set; }

    /// <summary>UTC time of the last sync failure, or null when the last cycle succeeded.</summary>
    public DateTimeOffset? LastErrorAtUtc { get; private set; }

    private string GatewayId => _gateway.Value.GatewayId;

    private SyncWorkerOptions Options => _options.Value;

    /// <summary>The local participant stamped on delivery acknowledgements (SYNC-FR-05).</summary>
    private Participant LocalReceiver => new()
    {
        Address = $"system:{GatewayId}",
        Kind = ParticipantKind.System,
        DisplayName = GatewayId,
        GatewayId = GatewayId,
    };

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sync worker starting for gateway {GatewayId}", GatewayId);

        // RECOVERING (product vision §10): reconcile the local store with Relay cursors before resuming sync.
        State = SyncWorkerState.Recovering;
        await ReconcileAsync(stoppingToken).ConfigureAwait(false);

        State = SyncWorkerState.Started;
        if (!_relay.IsConfigured)
        {
            _logger.LogInformation(
                "No Relay configured; outbound sync is disabled (offline-first). Outbox entries are retained for later sync.");
        }

        var consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                State = SyncWorkerState.Syncing;
                await SyncOnceAsync(stoppingToken).ConfigureAwait(false);
                State = SyncWorkerState.Started;

                consecutiveFailures = 0;
                LastErrorAtUtc = null;

                await Task.Delay(Options.PollInterval, _time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                LastErrorAtUtc = _time.GetUtcNow();
                State = SyncWorkerState.Started;

                var delay = Options.Backoff.ToPolicy().NextDelay(consecutiveFailures - 1);
                _logger.LogWarning(
                    ex,
                    "Sync cycle failed ({ConsecutiveFailures} consecutive failures); retrying in {RetryDelay}",
                    consecutiveFailures,
                    delay);

                try
                {
                    await Task.Delay(delay, _time, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        State = SyncWorkerState.Stopping;
        _logger.LogInformation("Sync worker stopping for gateway {GatewayId}", GatewayId);
        State = SyncWorkerState.Stopped;
    }

    /// <summary>
    /// Runs one sync cycle: push pending outbox entries, then pull and apply inbound batches (EDGE-FR-05).
    /// When no Relay is configured this is a no-op that leaves the outbox untouched.
    /// </summary>
    public async Task SyncOnceAsync(CancellationToken ct = default)
    {
        if (!_relay.IsConfigured || !_artifactTransfer.IsConfigured)
        {
            return;
        }

        await EnsureStateLoadedAsync(ct).ConfigureAwait(false);
        await PushAsync(ct).ConfigureAwait(false);
        await PullAsync(ct).ConfigureAwait(false);
        LastSyncAtUtc = _time.GetUtcNow();
    }

    private async Task PushAsync(CancellationToken ct)
    {
        var built = await _engine.BuildPushBatchAsync(new BuildPushBatchRequest
        {
            GatewayId = GatewayId,
            SinceCursor = _pushCursor,
            Limit = Options.BatchSize,
            Now = _time.GetUtcNow(),
            // Reuse the in-flight batch identity so a retry re-sends the same logical batch (SYNC-FR-02).
            BatchId = _inFlightBatchId,
            IdempotencyKey = _inFlightIdempotencyKey,
        }, ct).ConfigureAwait(false);

        // Idempotent retry (SYNC-FR-02, NF-05): the in-flight batch identity is valid only while it still
        // covers the same outbox entries. New entries are always appended at the end of the per-gateway
        // stream, so a changed AfterSequence watermark means the pending set grew while the push was in
        // flight. Rebuild with a fresh identity so the enlarged batch is a *new* logical batch — never fold
        // new messages into an already-(possibly-)sent batch's idempotency key, which would make the receiver
        // treat them as a replay and drop them.
        if (built.EntryIds.Count > 0 && _inFlightAfterSequence is { } inFlightSeq && built.AfterSequence != inFlightSeq)
        {
            built = await _engine.BuildPushBatchAsync(new BuildPushBatchRequest
            {
                GatewayId = GatewayId,
                SinceCursor = _pushCursor,
                Limit = Options.BatchSize,
                Now = _time.GetUtcNow(),
            }, ct).ConfigureAwait(false);
        }

        var batch = built.Batch;

        // Artifact bytes first, batch second (ARTF-FR-01): the Relay must hold every artifact a routed
        // message references before the batch carries the message, or a fast recipient could pull a message
        // whose bytes are not yet retrievable. Dedup: only hashes the Relay lacks are transferred.
        await UploadPendingArtifactsAsync(batch, ct).ConfigureAwait(false);

        if (built.EntryIds.Count > 0)
        {
            // Persist the in-flight batch identity *before* the network attempt, so a crash mid-push or a lost
            // acknowledgement reuses the same batchId + idempotencyKey on retry (SYNC-FR-02, NF-05).
            _inFlightBatchId = batch.BatchId;
            _inFlightIdempotencyKey = batch.IdempotencyKey;
            _inFlightAfterSequence = built.AfterSequence;
            await SaveCursorStateAsync(ct).ConfigureAwait(false);
        }

        var response = await _relay.PushAsync(batch, ct).ConfigureAwait(false);

        // Durable outbox flush: mark covered entries sent only after the Relay acknowledged the batch
        // (EDGE-FR-04). On a transport failure this line is never reached, so the entries stay pending.
        foreach (var entryId in built.EntryIds)
        {
            await _outbox.MarkSentAsync(entryId, ct).ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(response.Cursor))
        {
            _pushCursor = response.Cursor;
        }

        // Batch fully acked: clear the in-flight identity and durably advance the push cursor.
        _inFlightBatchId = null;
        _inFlightIdempotencyKey = null;
        _inFlightAfterSequence = null;
        await SaveCursorStateAsync(ct).ConfigureAwait(false);

        if (built.EntryIds.Count > 0)
        {
            _logger.LogInformation(
                "Pushed {EntryCount} outbox entries to Relay (batch {BatchId})",
                built.EntryIds.Count,
                batch.BatchId);
        }
    }

    private async Task PullAsync(CancellationToken ct)
    {
        var inbound = await _relay.PullAsync(_pullCursor, ct).ConfigureAwait(false);
        if (inbound is null)
        {
            return;
        }

        var applied = await _engine.ApplyBatchAsync(inbound, new ApplyBatchRequest
        {
            Receiver = LocalReceiver,
            Now = _time.GetUtcNow(),
        }, ct).ConfigureAwait(false);

        if (!applied.IsValid)
        {
            // A malformed batch from the Relay is logged and skipped; it is not applied and does not crash the
            // worker. The Relay is expected to converge by resending a valid batch.
            _logger.LogWarning(
                "Pulled batch {BatchId} failed validation ({Violations}); skipped",
                inbound.BatchId,
                string.Join(", ", applied.Violations));
            return;
        }

        if (!string.IsNullOrEmpty(applied.Cursor))
        {
            _pullCursor = applied.Cursor;
            await SaveCursorStateAsync(ct).ConfigureAwait(false);
        }

        // Enqueue the delivery acknowledgements produced by applying the batch so they flow back to the Relay
        // on the next push (SYNC-FR-05). Per-recipient ack attribution is refined in the synchronisation
        // feature's delivery-ack task.
        foreach (var ack in applied.DeliveryAcks)
        {
            await _outbox.EnqueueAsync(GatewayId, new SyncItem
            {
                Kind = SyncItemKind.Ack,
                Sequence = 0,
                Ack = ack,
            }, ct).ConfigureAwait(false);
        }

        if (applied.AppliedItems.Count > 0)
        {
            _logger.LogInformation(
                "Pulled and applied batch {BatchId} ({AppliedCount} items, cursor {Cursor})",
                inbound.BatchId,
                applied.AppliedItems.Count,
                _pullCursor);
        }

        // Download any artifact bytes the applied items reference that this gateway does not already hold
        // (dedup ARTF-FR-01). Resumable per artifact: a partial temp file survives a mid-way interruption and
        // the next cycle appends from its length; the bytes are content-hash verified before publishing.
        await DownloadInboundArtifactsAsync(applied.AppliedItems, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------------------------------
    // Artifact byte transfer (ARTF-FR-01/02, PROTO-FR-04 exception)
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Ensures the Relay holds the bytes of every artifact referenced by <paramref name="batch"/>. Dedup:
    /// only hashes the Relay reports missing are transferred (no re-transfer of known content, NF-03), and
    /// each upload is resumable chunk-wise (ARTF-FR-02). Runs <em>before</em> the batch is pushed so routed
    /// messages never reach a recipient whose artifact bytes are still absent at the Relay.
    /// </summary>
    private async Task UploadPendingArtifactsAsync(SyncBatch batch, CancellationToken ct)
    {
        var hashes = CollectArtifactHashes(batch.Items).Distinct().ToList();
        if (hashes.Count == 0)
        {
            return;
        }

        var missing = await _artifactTransfer.CheckHashesAsync(hashes, ct).ConfigureAwait(false);
        foreach (var hash in missing)
        {
            var size = await _artifactStore.GetSizeAsync(hash, ct).ConfigureAwait(false);
            if (size is null || size.Value <= 0)
            {
                // Metadata is registered but the bytes have not landed on disk yet (the PWA uploads in a
                // separate step) — skip this cycle; a later push picks it up.
                continue;
            }

            await using (var content = await _artifactStore.OpenReadAsync(hash, ct).ConfigureAwait(false))
            {
                if (content is null)
                {
                    continue;
                }

                await _artifactTransfer.UploadAsync(hash, size.Value, content, ct).ConfigureAwait(false);
            }

            _logger.LogInformation("Uploaded artifact {Hash} ({SizeBytes} bytes) to the Relay", hash, size.Value);
        }
    }

    /// <summary>
    /// Downloads artifact bytes referenced by freshly applied inbound items that this gateway does not hold.
    /// Each download writes to a partial temp file (resumable across interruptions, ARTF-FR-02) and is
    /// published through <see cref="IArtifactStore.SaveAsync"/>, which verifies the content hash before the
    /// bytes appear at a content-addressed path (SP-06). A corrupt partial is discarded and re-downloaded.
    /// </summary>
    private async Task DownloadInboundArtifactsAsync(IReadOnlyList<SyncItem> appliedItems, CancellationToken ct)
    {
        foreach (var hash in CollectArtifactHashes(appliedItems).Distinct())
        {
            // Dedup — already have the bytes, nothing to transfer (NF-03).
            if (await _artifactStore.ExistsAsync(hash, ct).ConfigureAwait(false))
            {
                continue;
            }

            if (await _artifactTransfer.GetRemoteSizeAsync(hash, ct).ConfigureAwait(false) is not { } remoteSize)
            {
                // The Relay does not hold the bytes yet (sender uploads in a later cycle) — retry next cycle.
                continue;
            }

            var partialPath = PartialDownloadPath(hash);
            var directory = Path.GetDirectoryName(partialPath)!;
            Directory.CreateDirectory(directory);

            try
            {
                await using (var sink = new FileStream(
                    partialPath,
                    FileMode.OpenOrCreate,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    sink.Seek(0, SeekOrigin.End); // resume: append past bytes already downloaded
                    var received = await _artifactTransfer.DownloadAsync(hash, sink, ct).ConfigureAwait(false);
                    if (received != remoteSize)
                    {
                        _logger.LogWarning(
                            "Download of artifact {Hash} is incomplete ({Received}/{Expected} bytes); retrying next cycle",
                            hash, received, remoteSize);
                        continue;
                    }
                }

                // Publish atomically with hash verification (SP-06); dedup if another cycle beat us to it.
                await using var content = new FileStream(
                    partialPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await _artifactStore.SaveAsync(content, hash, ct).ConfigureAwait(false);
                File.Delete(partialPath);

                _logger.LogInformation("Downloaded artifact {Hash} ({SizeBytes} bytes) from the Relay", hash, remoteSize);
            }
            catch (ArtifactHashMismatchException)
            {
                // The partial is corrupt (e.g. the source changed mid-transfer) — discard and re-download
                // from scratch on the next cycle.
                TryDelete(partialPath);
                throw;
            }
        }
    }

    /// <summary>All artifact content hashes referenced by a set of sync items (artifact items + message refs).</summary>
    private static IEnumerable<string> CollectArtifactHashes(IReadOnlyList<SyncItem>? items)
    {
        if (items is null)
        {
            yield break;
        }

        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case SyncItemKind.Artifact when item.Artifact is { } artifact:
                    if (artifact.Hash is { Length: > 0 })
                    {
                        yield return artifact.Hash;
                    }

                    break;
                case SyncItemKind.Message when item.Message is { } message:
                    foreach (var reference in message.ArtifactRefs ?? Enumerable.Empty<ArtifactReference>())
                    {
                        if (reference.Hash is { Length: > 0 })
                        {
                            yield return reference.Hash;
                        }
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// The partial-download path for an artifact hash, under the store's temp area so an interrupted download
    /// survives a worker restart and resumes by appending (ARTF-FR-02).
    /// </summary>
    private string PartialDownloadPath(string hash) =>
        Path.Combine(
            Path.GetTempPath(),
            "humangateway",
            "partials",
            ArtifactHash.RequireHex(hash)[..2],
            ArtifactHash.RequireHex(hash)[..]);


    /// <summary>
    /// RECOVERING (product vision §10): reconcile the local store with Relay cursors before resuming sync.
    /// The durable cursor state (push cursor, pull cursor, in-flight push batch identity) is reloaded from the
    /// <see cref="ISyncCursorStore"/>, so a restart resumes from the last cursor rather than re-pulling
    /// everything (NF-02, SYNC-FR-03) and a retried push reuses the same idempotency key (SYNC-FR-02).
    /// </summary>
    private Task ReconcileAsync(CancellationToken ct) => EnsureStateLoadedAsync(ct);

    /// <summary>Loads the durable cursor state exactly once (idempotent; the worker loop is single-threaded).</summary>
    private async Task EnsureStateLoadedAsync(CancellationToken ct)
    {
        if (_stateLoaded)
        {
            return;
        }

        var state = await _cursors.GetAsync(GatewayId, ct).ConfigureAwait(false);
        _pushCursor = state.PushCursor;
        _pullCursor = state.PullCursor;
        _inFlightBatchId = state.InFlightBatchId;
        _inFlightIdempotencyKey = state.InFlightIdempotencyKey;
        _inFlightAfterSequence = state.InFlightAfterSequence;
        _stateLoaded = true;
    }

    /// <summary>Persists the current cursor + in-flight state durably.</summary>
    private Task SaveCursorStateAsync(CancellationToken ct) => _cursors.SaveAsync(new SyncCursorState
    {
        GatewayId = GatewayId,
        PushCursor = _pushCursor,
        PullCursor = _pullCursor,
        InFlightBatchId = _inFlightBatchId,
        InFlightIdempotencyKey = _inFlightIdempotencyKey,
        InFlightAfterSequence = _inFlightAfterSequence,
    }, ct);

    /// <summary>Best-effort deletion of a partial download temp file.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leaked partial is harmless (resumed or overwritten next cycle).
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup (Windows file-lock window).
        }
    }
}
