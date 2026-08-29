using HumanGateway.Core.Batch;
using HumanGateway.Core.Cursor;
using HumanGateway.Core.Delivery;
using HumanGateway.Core.Idempotency;
using HumanGateway.Core.Ids;
using HumanGateway.Core.Inbox;
using HumanGateway.Core.Ordering;
using HumanGateway.Core.Outbox;
using HumanGateway.Core.Time;
using HumanGateway.Protocol.Models;

namespace HumanGateway.Core.Sync;

/// <summary>
/// Default <see cref="ISyncEngine"/> implementation. The decision logic is pure and deterministic (time is
/// injected, no clock reads, no randomness in decisions); durable state is read/written only through the
/// injected ports (<see cref="IOutbox"/>, <see cref="IInbox"/>, <see cref="IIdempotencyStore"/>).
/// </summary>
public sealed class SyncEngine : ISyncEngine
{
    private readonly IOutbox _outbox;
    private readonly IInbox _inbox;
    private readonly IIdempotencyStore _idempotency;

    /// <summary>Creates the engine over its durable ports.</summary>
    public SyncEngine(IOutbox outbox, IInbox inbox, IIdempotencyStore idempotency)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
    }

    /// <inheritdoc />
    public async Task<PushBatchResult> BuildPushBatchAsync(BuildPushBatchRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.GatewayId))
        {
            throw new ArgumentException("A gateway ID is required.", nameof(request));
        }

        var since = CursorCodec.TryDecode(request.SinceCursor) ?? CursorPosition.Start;
        var limit = Math.Clamp(request.Limit, 1, BatchSequenceValidator.MaxItemsPerBatch);

        var pending = await _outbox
            .GetPendingAsync(request.GatewayId, since.Sequence, limit, ct)
            .ConfigureAwait(false);

        // Deterministic reorder by sequence; the outbox is already sequence-ordered but reordering here makes
        // the engine independent of port ordering guarantees.
        var items = SequenceOrdering.Reorder(pending.Select(e => e.Item)).ToList();

        var batchId = request.BatchId ?? IdGenerator.NewId();
        var idempotencyKey = request.IdempotencyKey ?? IdempotencyKeys.Derive(batchId, items);

        var highWatermark = items.Count == 0
            ? since.Sequence
            : items.Max(i => i.Sequence);

        var batch = new SyncBatch
        {
            BatchId = batchId,
            GatewayId = request.GatewayId,
            Direction = BatchDirection.Push,
            SinceCursor = request.SinceCursor,
            // The receiver issues `cursor` in its response batch (syncbatch.schema.json); the sender leaves
            // it null here.
            Cursor = null,
            IdempotencyKey = idempotencyKey,
            SequenceStart = items.Count == 0 ? null : items.Min(i => i.Sequence),
            SequenceEnd = items.Count == 0 ? null : items.Max(i => i.Sequence),
            Items = items,
            CreatedAt = ProtocolTime.Format(request.Now),
        };

        return new PushBatchResult
        {
            Batch = batch,
            EntryIds = pending.Select(e => e.Id).ToList(),
            AfterSequence = highWatermark,
        };
    }

    /// <inheritdoc />
    public async Task<ApplyBatchResult> ApplyBatchAsync(SyncBatch batch, ApplyBatchRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(request);

        var validation = BatchSequenceValidator.Validate(batch);
        if (!validation.IsValid)
        {
            return new ApplyBatchResult { IsValid = false, Violations = validation.Violations };
        }

        // Deterministic reorder (SYNC-FR-07).
        var ordered = SequenceOrdering.Reorder(batch.Items ?? new List<SyncItem>()).ToList();

        var alreadyApplied = await _idempotency
            .WasAppliedAsync(batch.BatchId, batch.IdempotencyKey, ct)
            .ConfigureAwait(false);
        if (alreadyApplied)
        {
            // Replay: echo back the receiver's current contiguous position, with no further effect (SYNC-FR-02).
            var position = await CurrentPositionAsync(batch.GatewayId, ct).ConfigureAwait(false);
            return new ApplyBatchResult
            {
                IsValid = true,
                IsDuplicate = true,
                Position = position,
                Cursor = CursorCodec.Encode(position),
            };
        }

        // Per-message dedup: a message we have already received is not re-applied (SYNC-FR-02, NF-05).
        var appliedItems = new List<SyncItem>(ordered.Count);
        foreach (var item in ordered)
        {
            if (item.Kind == SyncItemKind.Message && item.Message is { } message)
            {
                var seen = await _inbox.ContainsMessageAsync(message.Id, ct).ConfigureAwait(false);
                if (seen)
                {
                    continue;
                }
            }

            await _inbox.AddAsync(new InboxEntry
            {
                Id = IdGenerator.NewId(),
                GatewayId = batch.GatewayId,
                Sequence = item.Sequence,
                Item = item,
                ReceivedAtUtc = request.Now,
            }, ct).ConfigureAwait(false);

            appliedItems.Add(item);
        }

        await _idempotency.RecordAsync(batch.BatchId, batch.IdempotencyKey, ct).ConfigureAwait(false);

        // The cursor is the highest contiguous applied sequence, derived from the *full* applied history (the
        // inbox, now including this batch's items) rather than only this batch — so a batch that fills a gap
        // converges the cursor in one step, and nothing past a gap is ever skipped (SYNC-FR-03/06/07).
        var newPosition = await CurrentPositionAsync(batch.GatewayId, ct).ConfigureAwait(false);

        var acks = DeliveryAckBuilder.BuildDeliveredAcks(appliedItems, request.Receiver, request.Now);

        return new ApplyBatchResult
        {
            IsValid = true,
            IsDuplicate = false,
            AppliedItems = appliedItems,
            DeliveryAcks = acks,
            Position = newPosition,
            Cursor = CursorCodec.Encode(newPosition),
        };
    }

    private async Task<CursorPosition> CurrentPositionAsync(string gatewayId, CancellationToken ct)
    {
        var sequences = await _inbox.GetSequencesAsync(gatewayId, ct).ConfigureAwait(false);
        return CursorMath.AdvanceContiguous(CursorPosition.Start, sequences);
    }
}
