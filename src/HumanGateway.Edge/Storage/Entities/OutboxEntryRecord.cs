using HumanGateway.Protocol.Models;

namespace HumanGateway.Edge.Storage.Entities;

/// <summary>
/// Durable outbox entry (EDGE-FR-04): a pending unit of outbound sync work committed to SQLite before any
/// network attempt. The full <see cref="SyncItem"/> envelope is stored as canonical JSON; the scalar columns
/// (<see cref="GatewayId"/>, <see cref="Sequence"/>, <see cref="SentAtUtc"/>) are denormalised for the pending
/// scan the sync worker runs. A non-null <see cref="SentAtUtc"/> marks the entry as delivered.
/// </summary>
public sealed class OutboxEntryRecord
{
    /// <summary>Durable outbox entry ID — the primary key.</summary>
    public string Id { get; set; } = null!;

    /// <summary>The gateway whose outbound stream this entry belongs to.</summary>
    public string GatewayId { get; set; } = null!;

    /// <summary>Per-gateway monotonic sequence number (≥ 1), allocated by <see cref="OutboxSequence"/>.</summary>
    public long Sequence { get; set; }

    /// <summary>The sync operation to send, stored as canonical wire JSON.</summary>
    public SyncItem Item { get; set; } = null!;

    /// <summary>When the entry was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Completed send attempts so far (≥ 0).</summary>
    public int Attempts { get; set; }

    /// <summary>Earliest time the next send attempt may run (backoff); null when due immediately.</summary>
    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    /// <summary>When the entry was successfully sent; null while still pending (unsent).</summary>
    public DateTimeOffset? SentAtUtc { get; set; }
}
