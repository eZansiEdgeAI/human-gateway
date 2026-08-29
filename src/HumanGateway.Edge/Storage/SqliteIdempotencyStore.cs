using HumanGateway.Core.Idempotency;
using HumanGateway.Edge.Storage.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HumanGateway.Edge.Storage;

/// <summary>
/// Durable SQLite <see cref="IIdempotencyStore"/> (SYNC-FR-02, NF-05): records applied batches under a unique
/// composite <c>(batchId, idempotencyKey)</c> key. Recording an already-applied batch is a no-op — the unique
/// constraint violation is swallowed — so a replayed batch is detected before any item is re-applied.
/// </summary>
public sealed class SqliteIdempotencyStore : IIdempotencyStore
{
    private readonly IDbContextFactory<EdgeDbContext> _factory;

    /// <summary>Creates the durable idempotency store over the context factory.</summary>
    public SqliteIdempotencyStore(IDbContextFactory<EdgeDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc />
    public async Task<bool> WasAppliedAsync(string batchId, string idempotencyKey, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        return await db.Idempotency
            .AsNoTracking()
            .AnyAsync(e => e.BatchId == batchId && e.IdempotencyKey == idempotencyKey, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordAsync(string batchId, string idempotencyKey, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        db.Idempotency.Add(new IdempotencyRecord
        {
            BatchId = batchId,
            IdempotencyKey = idempotencyKey,
            AppliedAtUtc = DateTimeOffset.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Already recorded (concurrent duplicate or replay) — the record is idempotent by contract.
        }
    }

    // SQLite primary result code: SQLITE_CONSTRAINT (19).
    private const int SqliteConstraint = 19;

    // SQLite extended result codes for a unique/PK violation (SQLITE_CONSTRAINT_UNIQUE / _PRIMARYKEY).
    private const int SqliteConstraintUnique = 2067;
    private const int SqliteConstraintPrimaryKey = 1555;

    /// <summary>Detects a SQLite unique/primary-key constraint violation from a save failure.</summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is not SqliteException sqlite || sqlite.SqliteErrorCode != SqliteConstraint)
            {
                continue;
            }

            return sqlite.SqliteExtendedErrorCode is SqliteConstraintUnique or SqliteConstraintPrimaryKey;
        }
        return false;
    }
}
