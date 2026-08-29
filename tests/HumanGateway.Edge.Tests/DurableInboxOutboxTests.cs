using HumanGateway.Core.Idempotency;
using HumanGateway.Core.Inbox;
using HumanGateway.Core.Outbox;
using HumanGateway.Core.Sync;
using HumanGateway.Edge.Storage;
using HumanGateway.Protocol.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace HumanGateway.Edge.Tests;

/// <summary>
/// Durable inbox/outbox/idempotency store tests (EDGE-FR-04, SYNC-FR-01/02, NF-05): verifies every create is
/// committed to SQLite (WAL) before any network attempt, sequence allocation is monotonic and collision-free
/// under concurrency (EDGE-FR-06), entries survive a process restart (EDGE-FR-07, local-edge §7 #4), and the
/// SyncEngine drives the durable ports to exactly-once delivery.
/// </summary>
public sealed class DurableInboxOutboxTests : IDisposable
{
    private const string GatewayId = "edge:00000000-0000-0000-0000-000000000001";

    private readonly string _dir;
    private readonly string _dbPath;

    public DurableInboxOutboxTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "hgdurable-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "edge.db");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of temp files; a leaked temp dir is harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup (Windows file-lock window).
        }
    }

    private string TestConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _dbPath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = true,
    }.ToString();

    /// <summary>
    /// Creates a fresh context factory (and therefore a fresh pool + WAL connection) so a test can simulate a
    /// full process restart by simply creating a second factory over the same file.
    /// </summary>
    private IDbContextFactory<EdgeDbContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<EdgeDbContext>()
            .UseSqlite(TestConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
        return new PooledDbContextFactory<EdgeDbContext>(options);
    }

    private static void Migrate(IDbContextFactory<EdgeDbContext> factory)
    {
        using var db = factory.CreateDbContext();
        db.Database.Migrate();
    }

    private static SyncItem MessageItem(long sequence, string id)
    {
        var message = TestData.NewMessage(id: id);
        return new SyncItem { Kind = SyncItemKind.Message, Sequence = sequence, Message = message };
    }

    private static SyncBatch MakeBatch(string batchId, string gatewayId, SyncItem[] items)
    {
        var ordered = items.OrderBy(i => i.Sequence).ToArray();
        return new SyncBatch
        {
            BatchId = batchId,
            GatewayId = gatewayId,
            Direction = BatchDirection.Push,
            IdempotencyKey = IdempotencyKeys.Derive(batchId, items),
            SequenceStart = ordered.Length == 0 ? null : ordered[0].Sequence,
            SequenceEnd = ordered.Length == 0 ? null : ordered[^1].Sequence,
            Items = ordered.ToList(),
            CreatedAt = "2026-08-29T00:00:00.000Z",
        };
    }

    // -----------------------------------------------------------------------------------------------
    // Outbox (EDGE-FR-04)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Outbox_Enqueue_CommitsDurably_AndAllocatesMonotonicSequence()
    {
        var factory = CreateFactory();
        Migrate(factory);
        var outbox = new SqliteOutbox(factory);

        var a = await outbox.EnqueueAsync(GatewayId, MessageItem(0, "msg-0001"));
        var b = await outbox.EnqueueAsync(GatewayId, MessageItem(0, "msg-0002"));
        var c = await outbox.EnqueueAsync(GatewayId, MessageItem(0, "msg-0003"));

        Assert.Equal(new long[] { 1, 2, 3 }, new[] { a.Sequence, b.Sequence, c.Sequence });

        var pending = await outbox.GetPendingAsync(GatewayId, 0, 100);
        Assert.Equal(new long[] { 1, 2, 3 }, pending.Select(e => e.Sequence).ToArray());
        Assert.Equal(new[] { "msg-0001", "msg-0002", "msg-0003" }, pending.Select(e => e.Item.Message!.Id).ToArray());
    }

    [Fact]
    public async Task Outbox_Sequence_IsUniqueUnderConcurrency()
    {
        var factory = CreateFactory();
        Migrate(factory);
        var outbox = new SqliteOutbox(factory);

        const int count = 32;
        var entries = await Task.WhenAll(
            Enumerable.Range(0, count).Select(i => outbox.EnqueueAsync(GatewayId, MessageItem(0, $"msg-{i:D4}"))));

        var sequences = entries.Select(e => e.Sequence).ToArray();
        Assert.Equal(count, sequences.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, count).Select(i => (long)i), sequences.OrderBy(s => s));
    }

    [Fact]
    public async Task Outbox_GetPending_RespectsCursorWatermark()
    {
        var factory = CreateFactory();
        Migrate(factory);
        var outbox = new SqliteOutbox(factory);

        for (var s = 1; s <= 5; s++)
        {
            await outbox.EnqueueAsync(GatewayId, MessageItem(s, $"msg-{s:D4}"));
        }

        var pending = await outbox.GetPendingAsync(GatewayId, afterSequence: 3, limit: 100);
        Assert.Equal(new long[] { 4, 5 }, pending.Select(e => e.Sequence).ToArray());
    }

    [Fact]
    public async Task Outbox_MarkSent_RemovesFromPendingScan()
    {
        var factory = CreateFactory();
        Migrate(factory);
        var outbox = new SqliteOutbox(factory);

        var a = await outbox.EnqueueAsync(GatewayId, MessageItem(0, "msg-0001"));
        await outbox.EnqueueAsync(GatewayId, MessageItem(0, "msg-0002"));

        await outbox.MarkSentAsync(a.Id);

        var pending = await outbox.GetPendingAsync(GatewayId, 0, 100);
        Assert.Equal(new[] { "msg-0002" }, pending.Select(e => e.Item.Message!.Id).ToArray());
    }

    [Fact]
    public async Task Outbox_MarkAttempt_UpdatesRetryMetadata()
    {
        var factory = CreateFactory();
        Migrate(factory);
        var outbox = new SqliteOutbox(factory);

        var a = await outbox.EnqueueAsync(GatewayId, MessageItem(0, "msg-0001"));
        var next = DateTimeOffset.UtcNow.AddMinutes(5);

        await outbox.MarkAttemptAsync(a.Id, attempts: 2, nextAttemptAtUtc: next);

        var pending = await outbox.GetPendingAsync(GatewayId, 0, 100);
        var entry = Assert.Single(pending);
        Assert.Equal(2, entry.Attempts);
        Assert.Equal(next, entry.NextAttemptAtUtc);
    }

    [Fact]
    public async Task Outbox_EntriesSurviveRestart()
    {
        // First "process": enqueue and commit.
        var factory1 = CreateFactory();
        Migrate(factory1);
        var outbox1 = new SqliteOutbox(factory1);
        await outbox1.EnqueueAsync(GatewayId, MessageItem(0, "msg-0001"));
        await outbox1.EnqueueAsync(GatewayId, MessageItem(0, "msg-0002"));

        // Second "process": a brand-new factory/pool over the same file (fresh WAL recovery on open).
        var factory2 = CreateFactory();
        var outbox2 = new SqliteOutbox(factory2);

        var pending = await outbox2.GetPendingAsync(GatewayId, 0, 100);
        Assert.Equal(new long[] { 1, 2 }, pending.Select(e => e.Sequence).ToArray());

        // The sequence counter also survived, so the next enqueue continues without collision.
        var next = await outbox2.EnqueueAsync(GatewayId, MessageItem(0, "msg-0003"));
        Assert.Equal(3, next.Sequence);
    }

    // -----------------------------------------------------------------------------------------------
    // Inbox (SYNC-FR-01/02)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Inbox_Add_DedupesByUniqueMessageIndex()
    {
        var factory = CreateFactory();
        Migrate(factory);
        var inbox = new SqliteInbox(factory);

        await inbox.AddAsync(NewInboxEntry(GatewayId, 1, MessageItem(1, "msg-0001")));

        Assert.True(await inbox.ContainsMessageAsync("msg-0001"));
        Assert.Single(await inbox.GetByMessageAsync("msg-0001"));

        // A second entry for the same message ID violates the unique index (exactly-once, NF-05).
        var duplicate = NewInboxEntry(GatewayId, 2, MessageItem(2, "msg-0001"));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => inbox.AddAsync(duplicate));
        Assert.Contains("UNIQUE constraint failed", ex.InnerException?.Message ?? ex.Message);
    }

    [Fact]
    public async Task Inbox_GetSequences_ReturnsDistinctAscending()
    {
        var factory = CreateFactory();
        Migrate(factory);
        var inbox = new SqliteInbox(factory);

        await inbox.AddAsync(NewInboxEntry(GatewayId, 3, MessageItem(3, "msg-0003")));
        await inbox.AddAsync(NewInboxEntry(GatewayId, 1, MessageItem(1, "msg-0001")));
        await inbox.AddAsync(NewInboxEntry(GatewayId, 2, MessageItem(2, "msg-0002")));

        Assert.Equal(new long[] { 1, 2, 3 }, await inbox.GetSequencesAsync(GatewayId));
    }

    // -----------------------------------------------------------------------------------------------
    // Idempotency (SYNC-FR-02)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Idempotency_Record_IsIdempotentAndDetectable()
    {
        var factory = CreateFactory();
        Migrate(factory);
        var store = new SqliteIdempotencyStore(factory);

        Assert.False(await store.WasAppliedAsync("batch-0001", "key-0001"));
        await store.RecordAsync("batch-0001", "key-0001");
        Assert.True(await store.WasAppliedAsync("batch-0001", "key-0001"));

        // Recording the same batch again is a no-op (no throw).
        await store.RecordAsync("batch-0001", "key-0001");
        Assert.True(await store.WasAppliedAsync("batch-0001", "key-0001"));
    }

    // -----------------------------------------------------------------------------------------------
    // End-to-end: SyncEngine over durable stores → exactly-once delivery (EDGE-FR-07)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task SyncEngine_OverDurableStores_DeliversExactlyOnce()
    {
        var factory = CreateFactory();
        Migrate(factory);

        var outbox = new SqliteOutbox(factory);
        var inbox = new SqliteInbox(factory);
        var idempotency = new SqliteIdempotencyStore(factory);
        var engine = new SyncEngine(outbox, inbox, idempotency);

        var receiver = TestData.Teacher;
        var now = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

        // Sender: commit the message to the durable outbox before any network attempt.
        await outbox.EnqueueAsync(GatewayId, MessageItem(0, "msg-0001"));

        var push = await engine.BuildPushBatchAsync(new BuildPushBatchRequest { GatewayId = GatewayId, Now = now });
        Assert.Single(push.Batch.Items!);

        // Receiver: apply the pushed batch, durably recording the message.
        var apply = await engine.ApplyBatchAsync(push.Batch, new ApplyBatchRequest { Receiver = receiver, Now = now });
        Assert.True(apply.IsValid);
        Assert.False(apply.IsDuplicate);
        Assert.True(await inbox.ContainsMessageAsync("msg-0001"));

        // Replay the same batch → idempotent, no second effect.
        var replay = await engine.ApplyBatchAsync(push.Batch, new ApplyBatchRequest { Receiver = receiver, Now = now });
        Assert.True(replay.IsDuplicate);
        Assert.Single(await inbox.GetByMessageAsync("msg-0001"));
    }

    private static InboxEntry NewInboxEntry(string gatewayId, long sequence, SyncItem item) => new()
    {
        Id = HumanGateway.Core.Ids.IdGenerator.NewId(),
        GatewayId = gatewayId,
        Sequence = sequence,
        Item = item,
        ReceivedAtUtc = DateTimeOffset.UtcNow,
    };
}
