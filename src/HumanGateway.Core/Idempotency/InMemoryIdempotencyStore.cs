namespace HumanGateway.Core.Idempotency;

/// <summary>
/// In-memory reference implementation of <see cref="IIdempotencyStore"/> (single-process; used by tests and
/// simple deployments). Durable deployments use a store backed by a unique <c>(batchId, idempotencyKey)</c>
/// index in SQLite/PostgreSQL.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <inheritdoc />
    public Task<bool> WasAppliedAsync(string batchId, string idempotencyKey, CancellationToken ct = default)
    {
        var key = CompositeKey(batchId, idempotencyKey);
        lock (_lock)
        {
            return Task.FromResult(_seen.Contains(key));
        }
    }

    /// <inheritdoc />
    public Task RecordAsync(string batchId, string idempotencyKey, CancellationToken ct = default)
    {
        var key = CompositeKey(batchId, idempotencyKey);
        lock (_lock)
        {
            _seen.Add(key);
        }
        return Task.CompletedTask;
    }

    private static string CompositeKey(string batchId, string idempotencyKey)
        => batchId + "\u0000" + idempotencyKey;
}
