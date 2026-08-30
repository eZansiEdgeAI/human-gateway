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
        IOptions<GatewayOptions> gateway,
        IOptions<SyncWorkerOptions> options,
        ILogger<SyncWorker> logger,
        TimeProvider? time = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _cursors = cursors ?? throw new ArgumentNullException(nameof(cursors));
        _relay = relay ?? throw new ArgumentNullException(nameof(relay));
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
        if (!_relay.IsConfigured)
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
    }

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
}
