using HumanGateway.Protocol.Models;

namespace HumanGateway.Relay.Storage.Entities;

/// <summary>
/// Durable inbox entry at the Relay (SYNC-FR-01): a unit of sync work received from a gateway and committed to
/// PostgreSQL. Messages are deduplicated by the denormalised <see cref="MessageId"/> column (unique index), so a
/// replayed batch has exactly one effect (SYNC-FR-02, NF-05). Entries are ordered by (gateway, sequence) for
/// deterministic reordering (SYNC-FR-07) and cursor computation (SYNC-FR-03).
/// </summary>
public sealed class InboxEntryRecord
{
    /// <summary>Durable inbox record ID — the primary key.</summary>
    public string Id { get; set; } = null!;

    /// <summary>The gateway this entry originated from.</summary>
    public string GatewayId { get; set; } = null!;

    /// <summary>Per-gateway monotonic sequence number (≥ 1).</summary>
    public long Sequence { get; set; }

    /// <summary>The durable message ID when this entry carries a message (null for delivery/artifact/ack items).</summary>
    public string? MessageId { get; set; }

    /// <summary>The received sync operation, stored as canonical wire JSON.</summary>
    public SyncItem Item { get; set; } = null!;

    /// <summary>When the entry was received.</summary>
    public DateTimeOffset ReceivedAtUtc { get; set; }
}
