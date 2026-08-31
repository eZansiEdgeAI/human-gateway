using HumanGateway.Protocol.Models;

namespace HumanGateway.Relay.Storage.Entities;

/// <summary>
/// Durable Relay outbox entry (RELAY-FR-04, SYNC-FR-03): a unit of sync work the Relay must deliver to a
/// registered gateway on its next pull. This is the per-gateway inbound (Relay → gateway) stream — the
/// "pull queue". Cross-school messages are routed here when a gateway pushes them, and delivery
/// acknowledgements (SYNC-FR-05) are routed back through here to the sender. Each entry carries a
/// per-gateway monotonic <see cref="Sequence"/> number (allocated from
/// <see cref="RelayOutboxSequenceRecord"/>) so the pull cursor is a contiguous high-watermark; a non-null
/// <see cref="DeliveredAtUtc"/> marks the entry as acknowledged by the gateway's echoed cursor (safe to
/// garbage-collect — entries at or below an echoed cursor are never selected again). The full
/// <see cref="SyncItem"/> envelope is stored as canonical wire JSON; <see cref="MessageId"/> is denormalised
/// so the unique <c>(gateway_id, message_id)</c> index deduplicates a routed message per gateway (a message
/// with several recipients at one gateway is delivered once).
/// </summary>
public sealed class RelayOutboxEntryRecord
{
    /// <summary>Durable outbox entry ID — the primary key.</summary>
    public string Id { get; set; } = null!;

    /// <summary>The gateway this item is addressed <em>to</em> (the pull stream owner).</summary>
    public string GatewayId { get; set; } = null!;

    /// <summary>Per-gateway monotonic sequence number (≥ 1), allocated by <see cref="RelayOutboxSequenceRecord"/>.</summary>
    public long Sequence { get; set; }

    /// <summary>The durable message ID when this entry carries a message (null for delivery/artifact/ack items).</summary>
    public string? MessageId { get; set; }

    /// <summary>The sync operation to deliver, stored as canonical wire JSON.</summary>
    public SyncItem Item { get; set; } = null!;

    /// <summary>When the entry was enqueued.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>When the gateway's echoed pull cursor passed this entry; null while still undelivered/pending.</summary>
    public DateTimeOffset? DeliveredAtUtc { get; set; }
}
