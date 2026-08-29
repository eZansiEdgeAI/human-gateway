using HumanGateway.Core.Hashing;
using HumanGateway.Core.Ids;
using HumanGateway.Core.Outbox;
using HumanGateway.Edge.Storage;
using HumanGateway.Protocol.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

// ---------------------------------------------------------------------------
// Crash probe (EDGE-FR-07, local-edge §6): continuously enqueues messages into
// the durable SQLite outbox until the parent test SIGKILLs it. Each committed
// enqueue is reported to stdout as "COMMITTED <n> <messageId> <sequence>" and
// flushed, so the parent knows exactly which writes were durably committed
// before the kill landed.
//
// Usage: dotnet HumanGateway.Edge.CrashProbe.dll <dbPath> [gatewayId]
// ---------------------------------------------------------------------------
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: CrashProbe <dbPath> [gatewayId]");
    return 1;
}

var dbPath = args[0];
var gatewayId = args.Length > 1 ? args[1] : "edge:crash-probe";

// Match the production wiring: pooled context factory over the WAL +
// synchronous=NORMAL connection (SqlitePragmaInterceptor applies the pragmas on
// every open). Pooling is intentionally enabled so the probe exercises the same
// connection lifetime as the running Edge service.
var connectionString = SqliteConnectionFactory.BuildConnectionString(dbPath);
var options = new DbContextOptionsBuilder<EdgeDbContext>()
    .UseSqlite(connectionString)
    .AddInterceptors(new SqlitePragmaInterceptor())
    .Options;
var factory = new PooledDbContextFactory<EdgeDbContext>(options);

// Ensure the schema exists (idempotent no-op if the parent already migrated).
await using (var db = await factory.CreateDbContextAsync())
{
    await db.Database.MigrateAsync();
}

var outbox = new SqliteOutbox(factory);

var n = 0;
while (true)
{
    n++;
    var messageId = $"crash-msg-{n:D6}";
    var entry = await outbox.EnqueueAsync(gatewayId, NewMessageItem(messageId));
    Console.Out.WriteLine($"COMMITTED {n} {messageId} {entry.Sequence}");
    await Console.Out.FlushAsync();
}

static SyncItem NewMessageItem(string id)
{
    var participant = new Participant
    {
        Address = "human:teacher@school.example",
        Kind = ParticipantKind.Human,
        DisplayName = "Teacher",
        UserId = "user:teacher",
    };
    var message = new Message
    {
        Id = id,
        Sender = participant,
        Recipients = new List<Participant> { participant },
        ConversationId = IdGenerator.NewId(),
        Payload = new MessagePayload { Body = "crash probe", Format = MessageFormat.Plaintext },
        CreatedAt = "2026-08-29T00:00:00.000Z",
    };
    return new SyncItem
    {
        Kind = SyncItemKind.Message,
        Sequence = 0,
        Message = message with { ContentHash = ContentHasher.ComputeMessageHash(message) },
    };
}

namespace HumanGateway.Edge.CrashProbe
{
    /// <summary>Marker type so the test assembly can locate this probe's compiled DLL.</summary>
    public static class CrashProbeMarker
    {
    }
}
