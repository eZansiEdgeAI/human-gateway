using HumanGateway.Core.Cursor;
using HumanGateway.Core.Delivery;
using HumanGateway.Core.Idempotency;
using HumanGateway.Core.Ids;
using HumanGateway.Core.Outbox;
using HumanGateway.Core.Sync;
using HumanGateway.Core.Time;
using HumanGateway.Protocol.Models;
using HumanGateway.Protocol.Validation;
using HumanGateway.Relay.Api;
using HumanGateway.Relay.Options;
using HumanGateway.Relay.Storage;
using HumanGateway.Relay.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using DeliveryEnvelope = HumanGateway.Protocol.Models.Delivery;

namespace HumanGateway.Relay.Services;

/// <summary>
/// The Relay's sync engine driver (RELAY-FR-02, SYNC-FR-01..07): applies inbound PUSH batches from registered
/// gateways and serves their PULL requests, consuming the shared <see cref="ISyncEngine"/> contract for cursor
/// math, idempotency, deterministic ordering, and delivery acknowledgements. The service owns the
/// cross-school routing side effect (product vision §6.3, RELAY-FR-04): applied messages are routed to the
/// recipient gateways' pull queues (the <c>relay_outbox</c>), and delivery acknowledgements (SYNC-FR-05) flow
/// back to the sender through the same queue — both schools only ever talk to the Relay (SP-01).
///
/// <para>Delivery is at-least-once end to end; exactly-once effect comes from idempotency on both sides
/// (NF-05): batch idempotency at the Relay (the engine's <see cref="IIdempotencyStore"/>), per-message
/// dedup, and the durable pull cursor the Edge echoes back. Routing and ack application are re-run on a
/// replayed batch — every side effect here is idempotent — so a retry after a partial failure converges
/// without duplication.</para>
/// </summary>
public sealed class RelaySyncService
{
    /// <summary>Max delivery attempts recorded at the Relay for a routed cross-site message (delivery.schema.json ≥ 1).</summary>
    private const long DefaultMaxAttempts = 5;

    /// <summary>The Relay's system participant — the receiving side that acknowledges a gateway's pushed messages.</summary>
    private static readonly Participant RelayReceiver = new()
    {
        Address = "system:relay",
        Kind = ParticipantKind.System,
        DisplayName = "HumanGateway Relay",
    };

    private readonly IDbContextFactory<RelayDbContext> _factory;
    private readonly GatewayService _gatewayService;
    private readonly ISyncEngine _engine;
    private readonly IOutbox _outbox;
    private readonly RelayOptions _options;
    private readonly ILogger<RelaySyncService> _logger;

    public RelaySyncService(
        IDbContextFactory<RelayDbContext> factory,
        GatewayService gatewayService,
        ISyncEngine engine,
        IOutbox outbox,
        IOptions<RelayOptions> options,
        ILogger<RelaySyncService> logger)
    {
        _factory = factory;
        _gatewayService = gatewayService;
        _engine = engine;
        _outbox = outbox;
        _options = options.Value;
        _logger = logger;
    }

    // -----------------------------------------------------------------------------------------------
    // PUSH: gateway → Relay
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Applies a gateway's PUSH batch (SYNC-FR-01..03): validates the shape, rejects unregistered identities
    /// (SP-02), applies the items idempotently through the sync engine, routes applied messages to the
    /// recipient gateways' pull queues (RELAY-FR-04), applies inbound delivery acknowledgements (SYNC-FR-05),
    /// and returns the result batch carrying the new push cursor. The response is a keepalive result batch
    /// (empty items, cursor only) — the cursor is the durable acknowledgement the Edge flushes its outbox on.
    /// </summary>
    public async Task<SyncBatch> PushAsync(SyncBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);

        // Shape validation against syncbatch.schema.json (SYNC-FR-01..07).
        var validation = ProtocolValidator.Default.SyncBatch.Validate(batch);
        if (!validation.IsValid)
        {
            throw GatewayServiceException.BadRequest(ErrorCodes.ValidationFailed,
                string.Join("; ", validation.Errors.Select(e => e.ToString())));
        }

        if (batch.Direction != BatchDirection.Push)
        {
            throw GatewayServiceException.BadRequest(ErrorCodes.ValidationFailed,
                $"Sync endpoint expects a {nameof(BatchDirection.Push)} batch; got '{batch.Direction}'.");
        }

        // SP-02: the pushing identity must be a REGISTERED gateway.
        await _gatewayService.RequireRegisteredAsync(batch.GatewayId, ct);

        var now = DateTimeOffset.UtcNow;

        // Apply through the sync engine: batch idempotency, deterministic reorder, per-message dedup, cursor.
        var result = await _engine.ApplyBatchAsync(batch, new ApplyBatchRequest
        {
            Receiver = RelayReceiver,
            Now = now,
        }, ct).ConfigureAwait(false);

        if (!result.IsValid)
        {
            throw GatewayServiceException.BadRequest(ErrorCodes.ValidationFailed,
                "The batch failed cross-field validation: " + string.Join(", ", result.Violations));
        }

        // Cross-school routing + delivery-ack application. These run on the *raw* batch items (not just the
        // newly applied ones) so a retry after a partial failure re-runs them idempotently — every side
        // effect below is a no-op once already done (NF-05).
        var items = batch.Items ?? new List<SyncItem>();
        if (items.Count > 0)
        {
            await RouteMessagesAsync(batch.GatewayId, items, now, ct).ConfigureAwait(false);
            await ApplyReceivedAcksAsync(items, now, ct).ConfigureAwait(false);
        }

        // Record the push cursor durably and refresh the rendezvous "online" watermark.
        await SaveCursorAsync(batch.GatewayId, result.Cursor, null, ct).ConfigureAwait(false);
        await _gatewayService.TouchLastSeenAsync(batch.GatewayId, now, ct).ConfigureAwait(false);

        if (result.AppliedItems.Count > 0)
        {
            _logger.LogInformation(
                "Applied push batch {BatchId} from {GatewayId} ({AppliedCount} new items, cursor {Cursor})",
                batch.BatchId, batch.GatewayId, result.AppliedItems.Count, result.Cursor);
        }
        else if (result.IsDuplicate)
        {
            _logger.LogInformation("Push batch {BatchId} from {GatewayId} was an idempotent replay", batch.BatchId, batch.GatewayId);
        }

        // The result batch: keepalive (empty items), carrying the push cursor the Edge must store.
        return new SyncBatch
        {
            BatchId = IdGenerator.NewId(),
            GatewayId = batch.GatewayId,
            Direction = BatchDirection.Push,
            SinceCursor = null,
            Cursor = result.Cursor,
            IdempotencyKey = IdempotencyKeys.Derive(batch.BatchId, Array.Empty<SyncItem>()),
            SequenceStart = null,
            SequenceEnd = null,
            Items = new List<SyncItem>(),
            CreatedAt = ProtocolTime.Format(now),
        };
    }

    // -----------------------------------------------------------------------------------------------
    // PULL: Relay → gateway
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Serves a gateway's PULL request (SYNC-FR-03): returns a PULL batch of the items the Relay has queued
    /// for the gateway after its echoed <paramref name="sinceCursor"/>, plus the new pull cursor. The cursor
    /// is issued as the high-watermark of the delivered batch, so a retry from a stale cursor re-sends the
    /// same items (at-least-once; the Edge's idempotent apply collapses them, NF-05) and nothing past the
    /// delivered span is ever skipped. The echoed cursor doubles as the acknowledgement that retires
    /// previously delivered entries.
    /// </summary>
    public async Task<SyncBatch> PullAsync(string gatewayId, string? sinceCursor, CancellationToken ct)
    {
        // SP-02: only a REGISTERED gateway may pull.
        await _gatewayService.RequireRegisteredAsync(gatewayId, ct);

        // Opaque-cursor handling: null = first exchange. A non-null token that is not a Relay-issued cursor
        // is a corrupt echo — reject with a retryable error so the Edge falls back to a fresh sync.
        var sincePosition = CursorPosition.Start;
        if (!string.IsNullOrEmpty(sinceCursor))
        {
            var decoded = CursorCodec.TryDecode(sinceCursor);
            if (decoded is null)
            {
                throw new GatewayServiceException(StatusCodes.Status400BadRequest, ErrorCodes.CursorInvalid,
                    "The sinceCursor token is not a Relay-issued cursor; resync from a null cursor (retryable).",
                    retryable: true);
            }
            sincePosition = decoded.Value;
        }

        var now = DateTimeOffset.UtcNow;

        // Acknowledge-and-retire: the echoed cursor proves the gateway durably received (and applied)
        // everything at or below it, so those pull-queue entries can be retired (SYNC-FR-03, at-least-once).
        if (sincePosition.Sequence > 0)
        {
            await AcknowledgeDeliveredAsync(gatewayId, sincePosition.Sequence, ct).ConfigureAwait(false);
        }

        // Build the pull batch through the sync engine (deterministic reorder + cursor math over the queue).
        var built = await _engine.BuildPushBatchAsync(new BuildPushBatchRequest
        {
            GatewayId = gatewayId,
            SinceCursor = sinceCursor,
            Limit = _options.Sync.PullBatchSize,
            Now = now,
        }, ct).ConfigureAwait(false);

        var batch = built.Batch;
        var pull = batch with
        {
            Direction = BatchDirection.Pull,
            // The pull cursor covers exactly what this batch delivered — never beyond it.
            Cursor = CursorCodec.Encode(new CursorPosition(built.AfterSequence)),
        };

        await SaveCursorAsync(gatewayId, null, pull.Cursor, ct).ConfigureAwait(false);
        await _gatewayService.TouchLastSeenAsync(gatewayId, now, ct).ConfigureAwait(false);

        if (built.EntryIds.Count > 0)
        {
            _logger.LogInformation(
                "Serving pull batch for {GatewayId} ({ItemCount} items, cursor {Cursor})",
                gatewayId, built.EntryIds.Count, pull.Cursor);
        }

        return pull;
    }

    // -----------------------------------------------------------------------------------------------
    // Cross-school routing (RELAY-FR-04)
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Routes applied message items to the recipient gateways' pull queues and records the durable
    /// cross-site delivery ledger. Idempotent: re-running on a replayed batch is a no-op for everything
    /// already routed (message/participant/delivery upserts are check-then-insert; the pull queue deduplicates
    /// a routed message per gateway via its unique index).
    /// </summary>
    private async Task RouteMessagesAsync(
        string pushingGatewayId,
        IEnumerable<SyncItem> items,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var routed = new HashSet<(string GatewayId, string MessageId)>();

        foreach (var item in items)
        {
            if (item.Kind != SyncItemKind.Message || item.Message is not { } message)
            {
                continue;
            }

            // Store the message envelope durably (RELAY-FR-01) and self-populate the participant directory
            // so human/agent recipients become resolvable for later routing and rendezvous.
            await UpsertMessageAsync(message, ct).ConfigureAwait(false);
            await UpsertParticipantsAsync(message, ct).ConfigureAwait(false);

            var senderGatewayId = await ResolveServingGatewayAsync(message.Sender, ct).ConfigureAwait(false)
                ?? pushingGatewayId;

            foreach (var recipient in message.Recipients ?? new List<Participant>())
            {
                var recipientGatewayId = await ResolveServingGatewayAsync(recipient, ct).ConfigureAwait(false);
                if (recipientGatewayId is null)
                {
                    _logger.LogWarning(
                        "Message {MessageId} recipient {RecipientAddress} is not routable to a registered gateway; skipped",
                        message.Id, recipient.Address);
                    continue;
                }

                if (recipientGatewayId == senderGatewayId)
                {
                    continue; // same-site recipient — no cloud hop needed
                }

                await EnsureDeliveryRecordAsync(message, recipient, now, ct).ConfigureAwait(false);

                if (!routed.Add((recipientGatewayId, message.Id)))
                {
                    continue; // several recipients at the same gateway → one pull item
                }

                await EnqueuePullItemAsync(recipientGatewayId, new SyncItem
                {
                    Kind = SyncItemKind.Message,
                    Sequence = 0,
                    Message = message,
                }, message.Id, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Applies the delivery acknowledgements carried by a pushed batch (SYNC-FR-05): transitions the Relay's
    /// cross-site delivery records and routes the acknowledgement back to the original sender's pull queue so
    /// it learns delivery on its next pull. Idempotent — the delivery transitioner never regresses state and
    /// the per-batch dedup set drops replayed acks.
    /// </summary>
    private async Task ApplyReceivedAcksAsync(IEnumerable<SyncItem> items, DateTimeOffset now, CancellationToken ct)
    {
        var seen = new HashSet<(string MessageId, string RecipientAddress)>();

        foreach (var item in items)
        {
            if (item.Kind != SyncItemKind.Ack || item.Ack is not { } ack)
            {
                continue;
            }

            if (!seen.Add((ack.MessageId, ack.Recipient.Address)))
            {
                continue; // replay of the same ack within one batch
            }

            await TransitionDeliveryFromAckAsync(ack, now, ct).ConfigureAwait(false);

            var senderGatewayId = await ResolveMessageSenderGatewayAsync(ack.MessageId, ct).ConfigureAwait(false);
            if (senderGatewayId is null)
            {
                _logger.LogWarning(
                    "Could not resolve the sender gateway for message {MessageId}; ack not routed back",
                    ack.MessageId);
                continue;
            }

            await EnqueuePullItemAsync(senderGatewayId, new SyncItem
            {
                Kind = SyncItemKind.Ack,
                Sequence = 0,
                Ack = ack,
            }, null, ct).ConfigureAwait(false);
        }
    }

    // -----------------------------------------------------------------------------------------------
    // Durability helpers
    // -----------------------------------------------------------------------------------------------

    /// <summary>Upserts a message envelope into the durable <c>messages</c> store (RELAY-FR-01). Idempotent.</summary>
    private async Task UpsertMessageAsync(Message message, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var exists = await db.Messages.AsNoTracking().AnyAsync(m => m.Id == message.Id, ct).ConfigureAwait(false);
        if (exists)
        {
            return;
        }

        db.Messages.Add(MessageRecord.FromEnvelope(message));
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { }
    }

    /// <summary>Self-populates the participant directory with every participant seen on a routed message.</summary>
    private async Task UpsertParticipantsAsync(Message message, CancellationToken ct)
    {
        foreach (var participant in new[] { message.Sender }.Concat(message.Recipients ?? new List<Participant>()))
        {
            if (participant?.Address is null)
            {
                continue;
            }

            await UpsertParticipantAsync(participant, ct).ConfigureAwait(false);
        }
    }

    private async Task UpsertParticipantAsync(Participant participant, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var exists = await db.Participants.AsNoTracking().AnyAsync(p => p.Address == participant.Address, ct).ConfigureAwait(false);
        if (exists)
        {
            return; // keep the first-seen metadata
        }

        db.Participants.Add(ParticipantRecord.FromParticipant(participant));
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { }
    }

    /// <summary>Creates the Relay's cross-site delivery ledger record for (message, recipient), QUEUED. Idempotent.</summary>
    private async Task EnsureDeliveryRecordAsync(Message message, Participant recipient, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var exists = await db.Deliveries.AsNoTracking().AnyAsync(
            d => d.MessageId == message.Id && d.RecipientAddress == recipient.Address, ct).ConfigureAwait(false);
        if (exists)
        {
            return;
        }

        var nowText = ProtocolTime.Format(now);
        var delivery = new DeliveryEnvelope
        {
            Id = IdGenerator.NewId(),
            MessageId = message.Id,
            Recipient = recipient,
            State = DeliveryState.Queued,
            Attempts = 0,
            MaxAttempts = DefaultMaxAttempts,
            QueuedAt = nowText,
            CreatedAt = nowText,
            UpdatedAt = nowText,
        };

        db.Deliveries.Add(DeliveryRecord.FromEnvelope(delivery));
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { }
    }

    /// <summary>
    /// Transitions the Relay's cross-site delivery record for a received acknowledgement (SYNC-FR-05).
    /// Idempotent via <see cref="DeliveryTransitioner.ApplyAck"/> (a replayed ack never regresses state).
    /// </summary>
    private async Task TransitionDeliveryFromAckAsync(DeliveryAck ack, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var record = await db.Deliveries.FirstOrDefaultAsync(
            d => d.MessageId == ack.MessageId && d.RecipientAddress == ack.Recipient.Address, ct).ConfigureAwait(false);
        if (record is null)
        {
            // The Relay never saw the original cross-site delivery — nothing to transition (e.g. a locally
            // delivered message being acked to the Relay). Not an error.
            return;
        }

        var result = ApplyAckThroughStates(record.Envelope, ack, now);
        if (!result.IsValid)
        {
            _logger.LogWarning(
                "Ack for message {MessageId} (recipient {RecipientAddress}) rejected: {Violation}",
                ack.MessageId, ack.Recipient.Address, result.Violation);
            return;
        }

        var next = result.Delivery!;
        record.Envelope = next;
        record.MessageId = next.MessageId;
        record.RecipientAddress = next.Recipient.Address;
        record.State = RelayJsonConversions.WireToken(next.State) ?? record.State;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a delivery acknowledgement to the Relay's cross-site delivery record, walking the delivery
    /// state machine forward when the record is still behind the ack's target. The record is created QUEUED
    /// when the message is routed; the recipient's acknowledgement may arrive before the Relay ever recorded
    /// the intermediate hops (product vision §10: QUEUED → SYNCING → DELIVERED → ACKNOWLEDGED), so a
    /// DELIVERED/ACKNOWLEDGED ack on a QUEUED record is applied after stepping through the legal chain.
    /// Idempotent: <see cref="DeliveryTransitioner"/> never regresses an already-satisfied delivery.
    /// </summary>
    private static DeliveryTransitionResult ApplyAckThroughStates(DeliveryEnvelope current, DeliveryAck ack, DateTimeOffset at)
    {
        var direct = DeliveryTransitioner.ApplyAck(current, ack, at);
        if (direct.IsValid)
        {
            return direct;
        }

        var target = ack.State switch
        {
            DeliveryAckState.Delivered => DeliveryState.Delivered,
            DeliveryAckState.Acknowledged => DeliveryState.Acknowledged,
            _ => DeliveryState.Failed,
        };

        var envelope = current;
        var guard = 0;
        while (envelope.State is { } from && from != target && !DeliveryStateMachine.IsTerminal(from) && guard++ < 8)
        {
            var hop = from switch
            {
                DeliveryState.Queued or DeliveryState.WaitingForSync => DeliveryState.Syncing,
                DeliveryState.Syncing => DeliveryState.Delivered,
                _ => target,
            };

            var stepped = DeliveryTransitioner.Transition(envelope, hop, at);
            if (!stepped.IsValid)
            {
                break;
            }

            envelope = stepped.Delivery!;
            var retried = DeliveryTransitioner.ApplyAck(envelope, ack, at);
            if (retried.IsValid)
            {
                return retried;
            }
        }

        return direct;
    }

    /// <summary>
    /// Retires pull-queue entries the gateway has acknowledged: everything at or below the echoed cursor.
    /// Entries past it stay pending and are re-sent on the next pull (at-least-once, NF-05).
    /// </summary>
    private async Task AcknowledgeDeliveredAsync(string gatewayId, long sequence, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var pending = await db.RelayOutbox
            .Where(e => e.GatewayId == gatewayId && e.DeliveredAtUtc == null && e.Sequence <= sequence)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in pending)
        {
            entry.DeliveredAtUtc = now;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Enqueues an item into a gateway's pull queue, deduplicated by (gateway, messageId).</summary>
    private async Task EnqueuePullItemAsync(string gatewayId, SyncItem item, string? messageId, CancellationToken ct)
    {
        // Check-then-insert with the unique (gateway_id, message_id) index as the hard backstop, so a replay
        // after a partial failure can never duplicate a routed message.
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (messageId is not null
            && await db.RelayOutbox.AsNoTracking().AnyAsync(
                e => e.GatewayId == gatewayId && e.MessageId == messageId, ct).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await _outbox.EnqueueAsync(gatewayId, item, ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Concurrent routing already enqueued it — one delivery per (gateway, message).
        }
    }

    /// <summary>Persists the per-gateway sync cursors (SYNC-FR-03) for observability.</summary>
    private async Task SaveCursorAsync(string gatewayId, string? pushCursor, string? pullCursor, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var record = await db.SyncCursors.FirstOrDefaultAsync(c => c.GatewayId == gatewayId, ct).ConfigureAwait(false);
        if (record is null)
        {
            db.SyncCursors.Add(new SyncCursorRecord
            {
                GatewayId = gatewayId,
                PushCursor = pushCursor,
                PullCursor = pullCursor,
            });
        }
        else
        {
            if (pushCursor is not null)
            {
                record.PushCursor = pushCursor;
            }

            if (pullCursor is not null)
            {
                record.PullCursor = pullCursor;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------------------------------
    // Participant → gateway resolution (WEBX-FR-02 routing)
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves a participant's serving gateway — a REGISTERED gateway only (SP-02). The participant's
    /// <c>GatewayId</c> identity link wins (participant.schema.json: a system participant "must equal the
    /// gatewayId of the sync batch carrying the message"); otherwise the typed address resolves via
    /// <see cref="ResolveGatewayIdForAddressAsync"/>.
    /// </summary>
    private async Task<string?> ResolveServingGatewayAsync(Participant participant, CancellationToken ct)
    {
        if (participant is null)
        {
            return null;
        }

        var gatewayId = participant.GatewayId is { } linked
            ? linked
            : await ResolveGatewayIdForAddressAsync(participant.Address, ct).ConfigureAwait(false);
        if (gatewayId is null)
        {
            return null;
        }

        return await _gatewayService.IsRegisteredAsync(gatewayId, ct).ConfigureAwait(false) ? gatewayId : null;
    }

    /// <summary>Resolves the sender gateway of a stored message (for routing its acks back).</summary>
    private async Task<string?> ResolveMessageSenderGatewayAsync(string messageId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var record = await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId, ct).ConfigureAwait(false);
        return record?.Envelope?.Sender is { } sender
            ? await ResolveServingGatewayAsync(sender, ct).ConfigureAwait(false)
            : null;
    }

    /// <summary>
    /// Resolves a typed address to its gateway ID: <c>system:&lt;gatewayId&gt;</c> maps directly (PROTO-FR-02);
    /// <c>human:</c>/<c>agent:</c> addresses resolve through the participant directory's <c>gatewayId</c>.
    /// </summary>
    private async Task<string?> ResolveGatewayIdForAddressAsync(string address, CancellationToken ct)
    {
        if (address.StartsWith("system:", StringComparison.Ordinal))
        {
            return address["system:".Length..];
        }

        // Human/agent addresses resolve through the participant directory (self-populated from routed messages).
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var participant = await db.Participants.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Address == address, ct)
            .ConfigureAwait(false);
        return participant?.GatewayId;
    }

    // -----------------------------------------------------------------------------------------------
    // Misc
    // -----------------------------------------------------------------------------------------------

    /// <summary>Detects a PostgreSQL unique/primary-key constraint violation from a save failure (SQLSTATE 23505).</summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        const string postgresUniqueViolation = "23505";

        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException postgres && postgres.SqlState == postgresUniqueViolation)
            {
                return true;
            }
        }

        return false;
    }
}
