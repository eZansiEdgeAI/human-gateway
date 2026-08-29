namespace HumanGateway.Core.Inbox;

/// <summary>
/// Durable inbox port (SYNC-FR-01): the receiving side records applied items so it can (a) deduplicate
/// replayed messages by durable ID (SYNC-FR-02) and (b) serve incremental sync from a cursor (SYNC-FR-03).
/// The SQLite/PostgreSQL implementations are owned by the edge/relay engineers.
/// </summary>
public interface IInbox
{
    /// <summary>Durably records a received item.</summary>
    Task AddAsync(InboxEntry entry, CancellationToken ct = default);

    /// <summary>Returns the inbox entries that reference the given message (dedup check).</summary>
    Task<IReadOnlyList<InboxEntry>> GetByMessageAsync(string messageId, CancellationToken ct = default);

    /// <summary>Returns true when a message with this durable ID has already been received (idempotent dedup).</summary>
    Task<bool> ContainsMessageAsync(string messageId, CancellationToken ct = default);

    /// <summary>
    /// Returns the distinct applied sequence numbers for a gateway, in ascending order. Used by the sync
    /// engine to compute the contiguous cursor from the full applied history (SYNC-FR-03/06/07).
    /// </summary>
    Task<IReadOnlyList<long>> GetSequencesAsync(string gatewayId, CancellationToken ct = default);
}
