using HumanGateway.Core.Outbox;
using HumanGateway.Edge.Storage.Entities;
using HumanGateway.Protocol.Models;
using Microsoft.EntityFrameworkCore;

namespace HumanGateway.Edge.Storage;

/// <summary>
/// Durable SQLite <see cref="IOutbox"/> (EDGE-FR-04): every enqueued item is committed to SQLite before any
/// network attempt. Sequence allocation is a single atomic <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c>
/// against <see cref="OutboxSequence"/>, so concurrent clients (EDGE-FR-06) get monotonic, collision-free
/// per-gateway sequences. Each operation opens a short-lived context from the injected factory, so a shared
/// store instance is safe across concurrent callers and survives restarts.
/// </summary>
public sealed class SqliteOutbox : IOutbox
{
    private readonly IDbContextFactory<EdgeDbContext> _factory;

    /// <summary>Creates the durable outbox over the context factory.</summary>
    public SqliteOutbox(IDbContextFactory<EdgeDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc />
    public async Task<OutboxEntry> EnqueueAsync(string gatewayId, SyncItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gatewayId);
        ArgumentNullException.ThrowIfNull(item);

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Sequence is allocated durably and atomically (before the entry row is written), so the commit of the
        // entry below is the durable write that precedes any network attempt. The shared OutboxWriter keeps the
        // sequence statement and pending-entry shape in one place.
        var record = await OutboxWriter.AddOutboxEntryAsync(db, gatewayId, item, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ToEntry(record);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(
        string gatewayId,
        long afterSequence,
        int limit,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var records = await db.Outbox
            .AsNoTracking()
            .Where(e => e.GatewayId == gatewayId && e.SentAtUtc == null && e.Sequence > afterSequence)
            .OrderBy(e => e.Sequence)
            .Take(Math.Max(0, limit))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return records.Select(ToEntry).ToList();
    }

    /// <inheritdoc />
    public async Task MarkSentAsync(string entryId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var record = await db.Outbox.SingleOrDefaultAsync(e => e.Id == entryId, ct).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        record.SentAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAttemptAsync(string entryId, int attempts, DateTimeOffset nextAttemptAtUtc, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var record = await db.Outbox.SingleOrDefaultAsync(e => e.Id == entryId, ct).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        record.Attempts = attempts;
        record.NextAttemptAtUtc = nextAttemptAtUtc;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Projects a stored outbox record to the core <see cref="OutboxEntry"/> shape.</summary>
    private static OutboxEntry ToEntry(OutboxEntryRecord record) => new()
    {
        Id = record.Id,
        GatewayId = record.GatewayId,
        Sequence = record.Sequence,
        Item = record.Item,
        CreatedAtUtc = record.CreatedAtUtc,
        Attempts = record.Attempts,
        NextAttemptAtUtc = record.NextAttemptAtUtc,
    };
}
