using HumanGateway.Protocol.Models;

namespace HumanGateway.Core.Outbox;

/// <summary>
/// A durable unit of pending outbound sync work (EDGE-FR-04, SYNC-FR-01). Each entry carries a monotonic
/// per-gateway sequence number (the deterministic ordering key) and is committed to durable storage before
/// any network attempt. Retry metadata (<see cref="Attempts"/>, <see cref="NextAttemptAtUtc"/>) drives the
/// backoff policy.
/// </summary>
public sealed record OutboxEntry
{
    /// <summary>Durable entry ID.</summary>
    public string Id { get; init; } = null!;

    /// <summary>The gateway whose outbound stream this entry belongs to.</summary>
    public string GatewayId { get; init; } = null!;

    /// <summary>Per-gateway monotonic sequence number (≥ 1).</summary>
    public long Sequence { get; init; }

    /// <summary>The sync operation to send.</summary>
    public SyncItem Item { get; init; } = null!;

    /// <summary>When the entry was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Completed send attempts so far (≥ 0).</summary>
    public int Attempts { get; init; }

    /// <summary>Earliest time the next send attempt may run (backoff); null when due immediately.</summary>
    public DateTimeOffset? NextAttemptAtUtc { get; init; }
}
