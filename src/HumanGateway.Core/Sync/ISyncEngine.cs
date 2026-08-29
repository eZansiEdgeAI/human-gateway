using HumanGateway.Core.Cursor;
using HumanGateway.Protocol.Models;

namespace HumanGateway.Core.Sync;

/// <summary>
/// The sync engine contract shared by the Edge Gateway and the Cloud Relay (product vision §6.3:
/// "cursor/sequence/idempotency handling shared by Edge and Relay"). The engine drives the durable ports
/// (<see cref="Outbox.IOutbox"/>, <see cref="Inbox.IInbox"/>, <see cref="Idempotency.IIdempotencyStore"/>)
/// but never performs I/O itself; its decision logic is pure and deterministic (time and randomness are
/// injected), so it is property-testable.
/// </summary>
public interface ISyncEngine
{
    /// <summary>
    /// Builds the next outbound PUSH batch for a gateway from its pending (unsent) outbox entries after the
    /// given cursor, reordered deterministically by sequence and stamped with a durable batch identity and a
    /// stable idempotency key (SYNC-FR-01..03, SYNC-FR-07). An empty pending queue yields a keepalive batch.
    /// </summary>
    Task<PushBatchResult> BuildPushBatchAsync(BuildPushBatchRequest request, CancellationToken ct = default);

    /// <summary>
    /// Applies an inbound batch: validates its shape, deduplicates a replayed batch (idempotency), reorders
    /// items by sequence, drops already-received messages, durably records the new items, advances the cursor
    /// contiguously, and produces delivery acknowledgements (SYNC-FR-01..03, SYNC-FR-05, SYNC-FR-07).
    /// </summary>
    Task<ApplyBatchResult> ApplyBatchAsync(SyncBatch batch, ApplyBatchRequest request, CancellationToken ct = default);
}

/// <summary>Inputs to <see cref="ISyncEngine.BuildPushBatchAsync"/>.</summary>
public sealed record BuildPushBatchRequest
{
    /// <summary>The gateway whose outbound stream to push.</summary>
    public string GatewayId { get; init; } = null!;

    /// <summary>The receiver's cursor, echoed back opaquely (null on the first exchange).</summary>
    public string? SinceCursor { get; init; }

    /// <summary>Maximum items to include (clamped to the batch item cap).</summary>
    public int Limit { get; init; } = 1000;

    /// <summary>When the batch is built (injected for determinism).</summary>
    public DateTimeOffset Now { get; init; }

    /// <summary>Reused batch identity on retry (null generates a fresh one).</summary>
    public string? BatchId { get; init; }

    /// <summary>Reused idempotency key on retry (null derives one from the batch's durable identity).</summary>
    public string? IdempotencyKey { get; init; }
}

/// <summary>Inputs to <see cref="ISyncEngine.ApplyBatchAsync"/>.</summary>
public sealed record ApplyBatchRequest
{
    /// <summary>The local participant acknowledging delivery (for delivery acks, SYNC-FR-05).</summary>
    public Participant Receiver { get; init; } = null!;

    /// <summary>When the batch is applied (injected for determinism).</summary>
    public DateTimeOffset Now { get; init; }
}

/// <summary>The result of building a push batch.</summary>
public sealed record PushBatchResult
{
    /// <summary>The batch to send.</summary>
    public SyncBatch Batch { get; init; } = null!;

    /// <summary>The outbox entry IDs covered by this batch (caller marks these sent once acked).</summary>
    public IReadOnlyList<string> EntryIds { get; init; } = Array.Empty<string>();

    /// <summary>The sequence watermark this batch advances the sender to (for retry bookkeeping).</summary>
    public long AfterSequence { get; init; }
}

/// <summary>The result of applying an inbound batch.</summary>
public sealed record ApplyBatchResult
{
    /// <summary>True when the batch passed shape validation.</summary>
    public bool IsValid { get; init; }

    /// <summary>Shape-violation codes when <see cref="IsValid"/> is false.</summary>
    public IReadOnlyList<string> Violations { get; init; } = Array.Empty<string>();

    /// <summary>True when the batch was already applied (idempotent replay) — no further effect.</summary>
    public bool IsDuplicate { get; init; }

    /// <summary>Newly applied items, reordered deterministically (empty for a keepalive or replay).</summary>
    public IReadOnlyList<SyncItem> AppliedItems { get; init; } = Array.Empty<SyncItem>();

    /// <summary>Delivery acknowledgements for the applied message items (SYNC-FR-05).</summary>
    public IReadOnlyList<DeliveryAck> DeliveryAcks { get; init; } = Array.Empty<DeliveryAck>();

    /// <summary>The receiver's new cursor position after applying this batch.</summary>
    public CursorPosition Position { get; init; }

    /// <summary>The receiver's new opaque cursor token (null at the start position).</summary>
    public string? Cursor { get; init; }
}
