using HumanGateway.Core.Cursor;
using HumanGateway.Edge.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace HumanGateway.Edge.Tests;

/// <summary>
/// Durable sync-cursor store tests (SYNC-FR-03, SYNC-FR-02): verifies the per-gateway push/pull cursors and
/// the in-flight push batch identity are committed to SQLite, round-trip exactly, and survive a process
/// restart — so a reconnect resumes from the last cursor (NF-02) and a retried push reuses the same batch
/// identity (idempotent retry).
/// </summary>
public sealed class SyncCursorStoreTests : IDisposable
{
    private const string GatewayId = "edge:00000000-0000-0000-0000-00000000cursor";

    private readonly string _dir;
    private readonly string _dbPath;

    public SyncCursorStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "hgcursor-tests-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task Get_ForUnknownGateway_ReturnsEmptyState()
    {
        var factory = CreateFactory();
        Migrate(factory);
        var store = new SqliteSyncCursorStore(factory);

        var state = await store.GetAsync(GatewayId);

        Assert.Equal(GatewayId, state.GatewayId);
        Assert.Null(state.PushCursor);
        Assert.Null(state.PullCursor);
        Assert.Null(state.InFlightBatchId);
        Assert.Null(state.InFlightIdempotencyKey);
        Assert.Null(state.InFlightAfterSequence);
    }

    [Fact]
    public async Task SaveAndGet_RoundTripsAllFields()
    {
        var factory = CreateFactory();
        Migrate(factory);
        var store = new SqliteSyncCursorStore(factory);

        var saved = new SyncCursorState
        {
            GatewayId = GatewayId,
            PushCursor = "v1:push-token",
            PullCursor = "v1:pull-token",
            InFlightBatchId = "batch-0001",
            InFlightIdempotencyKey = "key-0001",
            InFlightAfterSequence = 42,
        };

        await store.SaveAsync(saved);

        var loaded = await store.GetAsync(GatewayId);
        Assert.Equal(saved, loaded);
    }

    [Fact]
    public async Task Save_Upserts_ExistingGateway()
    {
        var factory = CreateFactory();
        Migrate(factory);
        var store = new SqliteSyncCursorStore(factory);

        await store.SaveAsync(new SyncCursorState { GatewayId = GatewayId, PushCursor = "v1:first", PullCursor = "v1:old" });
        await store.SaveAsync(new SyncCursorState { GatewayId = GatewayId, PushCursor = "v1:second", PullCursor = "v1:new" });

        // A second save updates the same row (upsert) with the new full snapshot.
        var loaded = await store.GetAsync(GatewayId);
        Assert.Equal("v1:second", loaded.PushCursor);
        Assert.Equal("v1:new", loaded.PullCursor);
    }

    [Fact]
    public async Task StateSurvivesRestart()
    {
        // First "process": persist cursor + in-flight state and commit.
        var factory1 = CreateFactory();
        Migrate(factory1);
        var store1 = new SqliteSyncCursorStore(factory1);
        await store1.SaveAsync(new SyncCursorState
        {
            GatewayId = GatewayId,
            PushCursor = "v1:push-token",
            PullCursor = "v1:pull-token",
            InFlightBatchId = "batch-0001",
            InFlightIdempotencyKey = "key-0001",
            InFlightAfterSequence = 42,
        });

        // Second "process": a brand-new factory/pool over the same file (fresh WAL recovery on open).
        var factory2 = CreateFactory();
        var store2 = new SqliteSyncCursorStore(factory2);

        var loaded = await store2.GetAsync(GatewayId);
        Assert.Equal("v1:push-token", loaded.PushCursor);
        Assert.Equal("v1:pull-token", loaded.PullCursor);
        Assert.Equal("batch-0001", loaded.InFlightBatchId);
        Assert.Equal("key-0001", loaded.InFlightIdempotencyKey);
        Assert.Equal(42, loaded.InFlightAfterSequence);
    }
}
