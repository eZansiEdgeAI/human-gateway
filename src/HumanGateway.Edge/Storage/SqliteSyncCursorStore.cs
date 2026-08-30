using HumanGateway.Core.Cursor;
using HumanGateway.Edge.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace HumanGateway.Edge.Storage;

/// <summary>
/// Durable SQLite <see cref="ISyncCursorStore"/> (SYNC-FR-03): persists each gateway's push/pull cursors and
/// the in-flight push batch identity (SYNC-FR-02). The worker reads this state on reconcile (so a reconnect
/// resumes from the last cursor, never full-state resync — NF-02) and writes it before/after each exchange
/// (so a retried push reuses the same <c>batchId</c> + <c>idempotencyKey</c>). Each operation opens a
/// short-lived context from the injected factory, so the store is safe across concurrent callers and restarts.
/// </summary>
public sealed class SqliteSyncCursorStore : ISyncCursorStore
{
    private readonly IDbContextFactory<EdgeDbContext> _factory;

    /// <summary>Creates the durable cursor store over the context factory.</summary>
    public SqliteSyncCursorStore(IDbContextFactory<EdgeDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc />
    public async Task<SyncCursorState> GetAsync(string gatewayId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gatewayId);

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var record = await db.SyncCursors
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.GatewayId == gatewayId, ct)
            .ConfigureAwait(false);

        return record is null ? SyncCursorState.Empty(gatewayId) : ToState(record);
    }

    /// <inheritdoc />
    public async Task SaveAsync(SyncCursorState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var record = await db.SyncCursors
            .SingleOrDefaultAsync(e => e.GatewayId == state.GatewayId, ct)
            .ConfigureAwait(false);

        if (record is null)
        {
            db.SyncCursors.Add(ToRecord(state));
        }
        else
        {
            record.PushCursor = state.PushCursor;
            record.PullCursor = state.PullCursor;
            record.InFlightBatchId = state.InFlightBatchId;
            record.InFlightIdempotencyKey = state.InFlightIdempotencyKey;
            record.InFlightAfterSequence = state.InFlightAfterSequence;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static SyncCursorState ToState(SyncCursorRecord record) => new()
    {
        GatewayId = record.GatewayId,
        PushCursor = record.PushCursor,
        PullCursor = record.PullCursor,
        InFlightBatchId = record.InFlightBatchId,
        InFlightIdempotencyKey = record.InFlightIdempotencyKey,
        InFlightAfterSequence = record.InFlightAfterSequence,
    };

    private static SyncCursorRecord ToRecord(SyncCursorState state) => new()
    {
        GatewayId = state.GatewayId,
        PushCursor = state.PushCursor,
        PullCursor = state.PullCursor,
        InFlightBatchId = state.InFlightBatchId,
        InFlightIdempotencyKey = state.InFlightIdempotencyKey,
        InFlightAfterSequence = state.InFlightAfterSequence,
    };
}
