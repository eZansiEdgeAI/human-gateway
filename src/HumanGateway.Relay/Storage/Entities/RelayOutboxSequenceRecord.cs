namespace HumanGateway.Relay.Storage.Entities;

/// <summary>
/// Durable per-gateway sequence counter for the Relay outbox pull stream (RELAY-FR-04, SYNC-FR-01/03).
/// Allocating a sequence number is a single atomic <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c>
/// against this table, so concurrent pushes never observe duplicate or non-monotonic sequences for the same
/// gateway. The counter only ever increases; a delivered entry's sequence is never reused, so a gateway's
/// pull-cursor watermark stays valid across Relay restarts.
/// </summary>
public sealed class RelayOutboxSequenceRecord
{
    /// <summary>The gateway whose inbound stream this counter tracks — the primary key.</summary>
    public string GatewayId { get; set; } = null!;

    /// <summary>The most recently allocated sequence number (≥ 0).</summary>
    public long LastSequence { get; set; }
}
