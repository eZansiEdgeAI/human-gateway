using HumanGateway.Core.Idempotency;
using HumanGateway.Relay.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HumanGateway.Relay.Storage;

/// <summary>
/// Durable PostgreSQL <see cref="IIdempotencyStore"/> (SYNC-FR-02, NF-05): records applied sync batches under
/// the unique composite <c>(batchId, idempotencyKey)</c> primary key. Recording an already-applied batch is a
/// no-op — the unique-constraint violation is swallowed — so a replayed batch (at-least-once transport) is
/// detected before any item is re-applied, yielding exactly-once effect.
/// </summary>
public sealed class RelayIdempotencyStore : IIdempotencyStore
{
    private readonly IDbContextFactory<RelayDbContext> _factory;

    /// <summary>Creates the durable idempotency store over the context factory.</summary>
    public RelayIdempotencyStore(IDbContextFactory<RelayDbContext> factory)
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

    /// <summary>Detects a PostgreSQL unique/primary-key constraint violation from a save failure.</summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // PostgreSQL error code 23505 = unique_violation (SQLSTATE class 23 integrity-constraint violation).
        const string PostgresUniqueViolation = "23505";

        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException postgres && postgres.SqlState == PostgresUniqueViolation)
            {
                return true;
            }
        }
        return false;
    }
}
