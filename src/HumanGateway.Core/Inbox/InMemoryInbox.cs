using HumanGateway.Core.Ids;

namespace HumanGateway.Core.Inbox;

/// <summary>
/// In-memory reference implementation of <see cref="IInbox"/> (single-process; used by tests and simple
/// deployments). Durable deployments use a SQLite/PostgreSQL-backed store with a unique index on message ID.
/// </summary>
public sealed class InMemoryInbox : IInbox
{
    private readonly List<InboxEntry> _entries = new();
    private readonly object _lock = new();

    /// <inheritdoc />
    public Task AddAsync(InboxEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            _entries.Add(entry);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InboxEntry>> GetByMessageAsync(string messageId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var matches = _entries
                .Where(e => e.Item.Message is { } m && m.Id == messageId)
                .ToList();
            return Task.FromResult<IReadOnlyList<InboxEntry>>(matches);
        }
    }

    /// <inheritdoc />
    public Task<bool> ContainsMessageAsync(string messageId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var found = _entries.Any(e => e.Item.Message is { } m && m.Id == messageId);
            return Task.FromResult(found);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<long>> GetSequencesAsync(string gatewayId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var sequences = _entries
                .Where(e => e.GatewayId == gatewayId)
                .Select(e => e.Sequence)
                .Distinct()
                .OrderBy(s => s)
                .ToList();
            return Task.FromResult<IReadOnlyList<long>>(sequences);
        }
    }
}
