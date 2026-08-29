using HumanGateway.Core.Ids;
using HumanGateway.Edge.Storage.Entities;
using HumanGateway.Protocol.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HumanGateway.Edge.Storage;

/// <summary>
/// Shared durable helpers for writing outbox entries (EDGE-FR-04): atomic per-gateway sequence allocation and
/// the pending-entry shape. <see cref="SqliteOutbox"/> writes through it directly, and it is available to any
/// caller that wants to commit a row and its outbox entry inside one SQLite transaction.
/// </summary>
public static class OutboxWriter
{
    /// <summary>
    /// Adds a pending outbox entry to <paramref name="db"/> (not yet saved), allocating the next per-gateway
    /// sequence number when the item's <see cref="SyncItem.Sequence"/> is unset (≤ 0). The caller owns the
    /// surrounding transaction and <c>SaveChangesAsync</c> call.
    /// </summary>
    public static async Task<OutboxEntryRecord> AddOutboxEntryAsync(
        EdgeDbContext db,
        string gatewayId,
        SyncItem item,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(gatewayId);
        ArgumentNullException.ThrowIfNull(item);

        var sequence = item.Sequence > 0
            ? item.Sequence
            : await AllocateSequenceAsync(db, gatewayId, ct).ConfigureAwait(false);

        var record = new OutboxEntryRecord
        {
            Id = IdGenerator.NewId(),
            GatewayId = gatewayId,
            Sequence = sequence,
            Item = item with { Sequence = sequence },
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Attempts = 0,
            NextAttemptAtUtc = null,
            SentAtUtc = null,
        };

        db.Outbox.Add(record);
        return record;
    }

    /// <summary>
    /// Atomically allocates the next per-gateway sequence number. The
    /// <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c> is a single statement, so SQLite serialises it and
    /// no two callers observe the same value — no read-modify-write race and no <c>SQLITE_BUSY</c> upgrade
    /// deadlock under concurrent writers (EDGE-FR-06).
    /// </summary>
    public static async Task<long> AllocateSequenceAsync(EdgeDbContext db, string gatewayId, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO outbox_sequences (gateway_id, last_sequence)
            VALUES (@gatewayId, 1)
            ON CONFLICT(gateway_id) DO UPDATE SET last_sequence = outbox_sequences.last_sequence + 1
            RETURNING last_sequence;
            """;

        return (await db.Database
            .SqlQueryRaw<long>(sql, new SqliteParameter("@gatewayId", gatewayId))
            .ToListAsync(ct)
            .ConfigureAwait(false))[0];
    }
}
