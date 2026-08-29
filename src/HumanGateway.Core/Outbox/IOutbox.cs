using HumanGateway.Protocol.Models;

namespace HumanGateway.Core.Outbox;

/// <summary>
/// Durable outbox port (EDGE-FR-04): every create is committed to durable storage before any network
/// attempt. The SQLite implementation is owned by the edge-engineer; the Relay's equivalent is owned by the
/// relay-engineer. The sync engine only drives this port — it never performs the I/O itself.
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Durably enqueues a sync item, assigning the next per-gateway sequence number if the item's
    /// <see cref="SyncItem.Sequence"/> is unset (≤ 0).
    /// </summary>
    Task<OutboxEntry> EnqueueAsync(string gatewayId, SyncItem item, CancellationToken ct = default);

    /// <summary>
    /// Returns pending (unsent) entries for a gateway with <see cref="OutboxEntry.Sequence"/> greater than
    /// <paramref name="afterSequence"/>, in ascending sequence order, up to <paramref name="limit"/>.
    /// The watermark enables cursor-based incremental push (never full-state resync, NF-02/SYNC-FR-03).
    /// </summary>
    Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(
        string gatewayId,
        long afterSequence,
        int limit,
        CancellationToken ct = default);

    /// <summary>Marks an entry as successfully sent.</summary>
    Task MarkSentAsync(string entryId, CancellationToken ct = default);

    /// <summary>Records a failed attempt and the backoff-deferred next attempt time.</summary>
    Task MarkAttemptAsync(string entryId, int attempts, DateTimeOffset nextAttemptAtUtc, CancellationToken ct = default);
}
