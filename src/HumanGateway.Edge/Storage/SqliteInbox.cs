using HumanGateway.Core.Inbox;
using HumanGateway.Edge.Storage.Entities;
using HumanGateway.Protocol.Models;
using Microsoft.EntityFrameworkCore;

namespace HumanGateway.Edge.Storage;

/// <summary>
/// Durable SQLite <see cref="IInbox"/> (SYNC-FR-01): received items are committed to SQLite and deduplicated by
/// message ID via the unique <c>ux_inbox_message_id</c> index (SYNC-FR-02, NF-05). Sequence history is kept
/// intact so the sync engine can compute the contiguous cursor from the full applied history (SYNC-FR-03/07).
/// </summary>
public sealed class SqliteInbox : IInbox
{
    private readonly IDbContextFactory<EdgeDbContext> _factory;

    /// <summary>Creates the durable inbox over the context factory.</summary>
    public SqliteInbox(IDbContextFactory<EdgeDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc />
    public async Task AddAsync(InboxEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        db.Inbox.Add(new InboxEntryRecord
        {
            Id = entry.Id,
            GatewayId = entry.GatewayId,
            Sequence = entry.Sequence,
            MessageId = MessageIdOf(entry.Item),
            Item = entry.Item,
            ReceivedAtUtc = entry.ReceivedAtUtc,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxEntry>> GetByMessageAsync(string messageId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var records = await db.Inbox
            .AsNoTracking()
            .Where(e => e.MessageId == messageId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return records.Select(ToEntry).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> ContainsMessageAsync(string messageId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        return await db.Inbox
            .AsNoTracking()
            .AnyAsync(e => e.MessageId == messageId, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<long>> GetSequencesAsync(string gatewayId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        return await db.Inbox
            .AsNoTracking()
            .Where(e => e.GatewayId == gatewayId)
            .Select(e => e.Sequence)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>Derives the dedup key from a sync item: the message ID for message items, otherwise null.</summary>
    private static string? MessageIdOf(SyncItem item) =>
        item.Kind == SyncItemKind.Message ? item.Message?.Id : null;

    private static InboxEntry ToEntry(InboxEntryRecord record) => new()
    {
        Id = record.Id,
        GatewayId = record.GatewayId,
        Sequence = record.Sequence,
        Item = record.Item,
        ReceivedAtUtc = record.ReceivedAtUtc,
    };
}
