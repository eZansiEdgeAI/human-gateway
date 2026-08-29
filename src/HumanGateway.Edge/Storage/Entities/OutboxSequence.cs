namespace HumanGateway.Edge.Storage.Entities;

/// <summary>
/// Durable per-gateway sequence counter (EDGE-FR-04, SYNC-FR-01). Allocating a sequence number is a single
/// atomic <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c> against this table, so concurrent local
/// clients (EDGE-FR-06) never observe duplicate or non-monotonic sequences. The counter only ever increases;
/// a sent entry's sequence is never reused, so a receiver's cursor watermark stays valid after restarts.
/// </summary>
public sealed class OutboxSequence
{
    /// <summary>The gateway whose outbound stream this counter tracks — the primary key.</summary>
    public string GatewayId { get; set; } = null!;

    /// <summary>The most recently allocated sequence number (≥ 0).</summary>
    public long LastSequence { get; set; }
}
