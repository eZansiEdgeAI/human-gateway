namespace HumanGateway.Core.Cursor;

/// <summary>
/// In-memory reference implementation of <see cref="ISyncCursorStore"/> (single-process; used by tests and
/// simple deployments). Durable deployments use a SQLite/PostgreSQL-backed store. State is keyed by gateway
/// ID and thread-safe under concurrent access.
/// </summary>
public sealed class InMemorySyncCursorStore : ISyncCursorStore
{
    private readonly Dictionary<string, SyncCursorState> _states = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <inheritdoc />
    public Task<SyncCursorState> GetAsync(string gatewayId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gatewayId);
        lock (_lock)
        {
            return Task.FromResult(
                _states.TryGetValue(gatewayId, out var state) ? state : SyncCursorState.Empty(gatewayId));
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(SyncCursorState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_lock)
        {
            // SyncCursorState is immutable, so storing the reference is safe.
            _states[state.GatewayId] = state;
        }
        return Task.CompletedTask;
    }
}
