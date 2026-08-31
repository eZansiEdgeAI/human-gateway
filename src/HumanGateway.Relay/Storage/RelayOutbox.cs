using HumanGateway.Core.Ids;
using HumanGateway.Core.Outbox;
using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HumanGateway.Relay.Storage;

/// <summary>
/// Durable PostgreSQL <see cref="IOutbox"/> for the Relay (RELAY-FR-04, SYNC-FR-03): the per-gateway
/// <em>pull queue</em> — items the Relay must deliver to a registered gateway on its next pull. This is the
/// inbound (Relay → gateway) stream, driven by the sync engine's <c>BuildPushBatchAsync</c> when the gateway
/// pulls. Sequence allocation is a single atomic <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c>
/// against <see cref="RelayOutboxSequenceRecord"/>, so concurrent pushes never observe duplicate or
/// non-monotonic sequences for the same gateway. Delivery is at-least-once: entries stay pending until the
/// gateway's <em>echoed pull cursor</em> acknowledges them (<see cref="MarkSentAsync"/>), so a lost or
/// malformed pull response is simply re-sent on the next pull and the receiving gateway's own idempotency
/// collapses it (NF-05).
/// </summary>
public sealed class RelayOutbox : IOutbox
{
    private readonly IDbContextFactory<RelayDbContext> _factory;

    /// <summary>Creates the durable outbox over the context factory.</summary>
    public RelayOutbox(IDbContextFactory<RelayDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc />
    public async Task<OutboxEntry> EnqueueAsync(string gatewayId, SyncItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gatewayId);
        ArgumentNullException.ThrowIfNull(item);

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // The sequence is allocated durably and atomically (before the entry row is written), so the commit
        // below is the durable write that precedes any delivery attempt.
        var sequence = item.Sequence > 0
            ? item.Sequence
            : await AllocateSequenceAsync(db, gatewayId, ct).ConfigureAwait(false);

        var record = new RelayOutboxEntryRecord
        {
            Id = IdGenerator.NewId(),
            GatewayId = gatewayId,
            Sequence = sequence,
            MessageId = MessageIdOf(item),
            Item = item with { Sequence = sequence },
            CreatedAtUtc = DateTimeOffset.UtcNow,
            DeliveredAtUtc = null,
        };

        db.RelayOutbox.Add(record);
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

        var records = await db.RelayOutbox
            .AsNoTracking()
            .Where(e => e.GatewayId == gatewayId && e.DeliveredAtUtc == null && e.Sequence > afterSequence)
            .OrderBy(e => e.Sequence)
            .Take(Math.Max(0, limit))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return records.Select(ToEntry).ToList();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Marks the entry acknowledged. The Relay calls this for every pull-queue entry at or below the
    /// gateway's <em>echoed</em> pull cursor — the gateway's durable cursor advance is its acknowledgement
    /// that it received (and applied) everything up to that point, so the entry is safe to retire (SYNC-FR-03,
    /// at-least-once → exactly-once via the gateway's idempotent apply).
    /// </remarks>
    public async Task MarkSentAsync(string entryId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var record = await db.RelayOutbox.SingleOrDefaultAsync(e => e.Id == entryId, ct).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        record.DeliveredAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Not applicable to the Relay pull queue: delivery is retried by the gateway's next pull from its echoed
    /// cursor (at-least-once), so no per-entry backoff metadata is kept. Implemented as a no-op to satisfy the
    /// shared port contract.
    /// </remarks>
    public Task MarkAttemptAsync(string entryId, int attempts, DateTimeOffset nextAttemptAtUtc, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// Atomically allocates the next per-gateway sequence number. The
    /// <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c> is a single statement, so PostgreSQL serialises
    /// it and no two callers observe the same value — no read-modify-write race under concurrent pushes.
    /// </summary>
    public static async Task<long> AllocateSequenceAsync(RelayDbContext db, string gatewayId, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO relay_outbox_sequences (gateway_id, last_sequence)
            VALUES (@gatewayId, 1)
            ON CONFLICT(gateway_id) DO UPDATE SET last_sequence = relay_outbox_sequences.last_sequence + 1
            RETURNING last_sequence;
            """;

        return (await db.Database
            .SqlQueryRaw<long>(sql, new NpgsqlParameter("@gatewayId", gatewayId))
            .ToListAsync(ct)
            .ConfigureAwait(false))[0];
    }

    /// <summary>Derives the dedup key from a sync item: the message ID for message items, otherwise null.</summary>
    private static string? MessageIdOf(SyncItem item) =>
        item.Kind == SyncItemKind.Message ? item.Message?.Id : null;

    private static OutboxEntry ToEntry(RelayOutboxEntryRecord record) => new()
    {
        Id = record.Id,
        GatewayId = record.GatewayId,
        Sequence = record.Sequence,
        Item = record.Item,
        CreatedAtUtc = record.CreatedAtUtc,
        Attempts = 0,
        NextAttemptAtUtc = null,
    };
}
