using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using HumanGateway.Edge.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace HumanGateway.Edge.Tests;

/// <summary>
/// Crash-consistency tests (EDGE-FR-07, local-edge §6): kills the Edge process with SIGKILL mid-write, then
/// restarts over the same SQLite file and verifies that every write durably committed before the kill is
/// present exactly once (no loss, no duplication) and the WAL recovered cleanly.
/// </summary>
/// <remarks>
/// The probe (<c>HumanGateway.Edge.CrashProbe</c>) is a separate process that continuously enqueues messages
/// into the durable outbox and reports each committed enqueue to stdout. Killing that process with SIGKILL
/// exercises the real WAL-recovery path — the same one the Edge service takes on a power loss or process kill.
/// </remarks>
public sealed class CrashConsistencyTests : IDisposable
{
    private const string GatewayId = "edge:00000000-0000-0000-0000-00000000crash";

    private readonly string _dir;
    private readonly string _dbPath;

    public CrashConsistencyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "hgcrash-tests-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task KillDuringWrite_CommittedMessagesSurviveExactlyOnce()
    {
        var probeDll = typeof(HumanGateway.Edge.CrashProbe.CrashProbeMarker).Assembly.Location;

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(probeDll);
        startInfo.ArgumentList.Add(_dbPath);
        startInfo.ArgumentList.Add(GatewayId);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var committed = new ConcurrentQueue<Committed>();
        var stderr = new StringBuilder();

        var stdoutTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                var parts = line.Split(' ');
                if (parts.Length >= 4 && parts[0] == "COMMITTED")
                {
                    committed.Enqueue(new Committed(int.Parse(parts[1]), parts[2], long.Parse(parts[3])));
                }
            }
        });

        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                stderr.AppendLine(line);
            }
        });

        const int threshold = 6;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (committed.Count < threshold && !process.HasExited && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(
            committed.Count >= threshold,
            $"Crash probe committed {committed.Count} messages before the kill (expected >= {threshold}). stderr:\n{stderr}");

        // kill -9 mid-write: SIGKILL the probe while it is actively enqueueing.
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        await process.WaitForExitAsync();
        await stdoutTask;
        await stderrTask;

        // "Restart": a brand-new context factory/pool over the same file. Migrate() is a no-op here, but
        // opening the connection is what drives WAL recovery — if the file were corrupt, this would throw.
        var factory = CreateFactory();
        Migrate(factory);

        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Outbox
            .AsNoTracking()
            .OrderBy(e => e.Sequence)
            .ToListAsync();

        var observed = committed.ToList();
        var countByMessageId = rows
            .Where(e => e.Item.Message is not null)
            .GroupBy(e => e.Item.Message!.Id)
            .ToDictionary(g => g.Key, g => g.Count());

        // Every durably-committed message must be present exactly once (EDGE-FR-07: no loss, no duplication).
        foreach (var c in observed)
        {
            var count = countByMessageId.TryGetValue(c.MessageId, out var found) ? found : 0;
            Assert.True(
                count == 1,
                $"Committed message {c.MessageId} (n={c.N}) must be present exactly once after restart; found {count}.");
        }

        // No duplicate sequence numbers survived WAL recovery (the unique index + atomic allocation hold).
        var sequences = rows.Select(e => e.Sequence).ToList();
        Assert.Equal(sequences.Count, sequences.Distinct().Count());
    }

    private readonly record struct Committed(int N, string MessageId, long Sequence);

    private string TestConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _dbPath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = false,
    }.ToString();

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
}
