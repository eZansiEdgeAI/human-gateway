using HumanGateway.Core.Ids;
using HumanGateway.Protocol.Models;

namespace HumanGateway.Core.Outbox;

/// <summary>
/// In-memory reference implementation of <see cref="IOutbox"/> (single-process; used by tests and simple
/// deployments). Durable deployments use a SQLite/PostgreSQL-backed store. Sequence allocation is monotonic
/// per gateway and thread-safe.
/// </summary>
public sealed class InMemoryOutbox : IOutbox
{
    private readonly List<OutboxEntry> _entries = new();
    private readonly Dictionary<string, long> _nextSequence = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <inheritdoc />
    public Task<OutboxEntry> EnqueueAsync(string gatewayId, SyncItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gatewayId);
        ArgumentNullException.ThrowIfNull(item);

        lock (_lock)
        {
            var sequence = item.Sequence > 0
                ? item.Sequence
                : AllocateSequence(gatewayId);

            var entry = new OutboxEntry
            {
                Id = IdGenerator.NewId(),
                GatewayId = gatewayId,
                Sequence = sequence,
                Item = item with { Sequence = sequence },
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Attempts = 0,
                NextAttemptAtUtc = null,
            };
            _entries.Add(entry);
            return Task.FromResult(entry);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(
        string gatewayId,
        long afterSequence,
        int limit,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            var pending = _entries
                .Where(e => e.GatewayId == gatewayId && e.Sequence > afterSequence)
                .OrderBy(e => e.Sequence)
                .Take(Math.Max(0, limit))
                .ToList();
            return Task.FromResult<IReadOnlyList<OutboxEntry>>(pending);
        }
    }

    /// <inheritdoc />
    public Task MarkSentAsync(string entryId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var index = _entries.FindIndex(e => e.Id == entryId);
            if (index >= 0)
            {
                _entries.RemoveAt(index);
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkAttemptAsync(string entryId, int attempts, DateTimeOffset nextAttemptAtUtc, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var index = _entries.FindIndex(e => e.Id == entryId);
            if (index >= 0)
            {
                _entries[index] = _entries[index] with { Attempts = attempts, NextAttemptAtUtc = nextAttemptAtUtc };
            }
        }
        return Task.CompletedTask;
    }

    private long AllocateSequence(string gatewayId)
    {
        _nextSequence.TryGetValue(gatewayId, out var current);
        var next = current + 1;
        _nextSequence[gatewayId] = next;
        return next;
    }
}
