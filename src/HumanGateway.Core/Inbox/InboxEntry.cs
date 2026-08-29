using HumanGateway.Protocol.Models;

namespace HumanGateway.Core.Inbox;

/// <summary>
/// A durably-received unit of sync work (SYNC-FR-01). Messages are deduplicated by their durable
/// <see cref="Message.Id"/> so a replayed batch produces exactly one effect (NF-05, SYNC-FR-02). Entries are
/// ordered by sequence number so receivers reorder deterministically (SYNC-FR-07).
/// </summary>
public sealed record InboxEntry
{
    /// <summary>Durable inbox record ID.</summary>
    public string Id { get; init; } = null!;

    /// <summary>The gateway this entry originated from.</summary>
    public string GatewayId { get; init; } = null!;

    /// <summary>Per-gateway monotonic sequence number (≥ 1).</summary>
    public long Sequence { get; init; }

    /// <summary>The received sync operation.</summary>
    public SyncItem Item { get; init; } = null!;

    /// <summary>When the entry was received.</summary>
    public DateTimeOffset ReceivedAtUtc { get; init; }
}
