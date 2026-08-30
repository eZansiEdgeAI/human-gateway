using System.Text.Json;
using HumanGateway.Core.Ids;
using HumanGateway.Edge.Storage;
using HumanGateway.Edge.Storage.Entities;
using HumanGateway.Protocol.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HumanGateway.Edge.Tests;

/// <summary>
/// SQLite store tests (EDGE-FR-02, NF-04): verifies the WAL + synchronous=NORMAL durability pragmas are
/// applied on every open, the EF Core migration creates the expected schema, and every entity type
/// round-trips through the store with canonical-JSON envelopes and denormalised query columns intact.
/// </summary>
/// <remarks>
/// Each test uses its own throwaway database file in a unique temp directory with pooling disabled, so tests
/// run in parallel without contending on a shared file and cleanup is a plain directory delete.
/// </remarks>
public sealed class EdgeDbContextTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;

    public EdgeDbContextTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "hgedge-tests-" + Guid.NewGuid().ToString("N"));
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
        Pooling = false,
    }.ToString();

    private EdgeDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EdgeDbContext>()
            .UseSqlite(TestConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
        return new EdgeDbContext(options);
    }

    [Fact]
    public void SqliteConnectionFactory_AppliesDurabilityPragmas()
    {
        using var connection = SqliteConnectionFactory.Open(TestConnectionString);

        Assert.Equal("wal", Scalar(connection, "PRAGMA journal_mode"));
        Assert.Equal("1", Scalar(connection, "PRAGMA synchronous")); // synchronous=NORMAL
        Assert.Equal("1", Scalar(connection, "PRAGMA foreign_keys"));
        Assert.Equal("5000", Scalar(connection, "PRAGMA busy_timeout"));
    }

    [Fact]
    public void Migrate_CreatesExpectedTables()
    {
        using var db = CreateContext();
        db.Database.Migrate();

        var tables = db.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EFMigrations%' ORDER BY name")
            .ToList();

        Assert.Equal(
            new[]
            {
                "artifacts",
                "conversation_participants",
                "conversations",
                "deliveries",
                "idempotency",
                "inbox",
                "messages",
                "outbox",
                "outbox_sequences",
                "participants",
                "sync_cursors",
                "tasks",
            },
            tables);
    }

    [Fact]
    public void ParticipantAndConversation_RoundTrip()
    {
        using var db = CreateContext();
        db.Database.Migrate();

        var teacher = TestData.Teacher;
        var conversation = TestData.NewConversation(teacher);

        db.Participants.Add(ParticipantRecord.FromParticipant(teacher));
        db.Conversations.Add(conversation);
        db.SaveChanges();

        // Participant directory round-trips its canonical JSON envelope + denormalised columns.
        var savedParticipant = db.Participants.Single(p => p.Address == teacher.Address);
        Assert.Equal("human", savedParticipant.Kind);
        Assert.Equal("Teacher", savedParticipant.DisplayName);
        Assert.Equal("user:teacher", savedParticipant.UserId);
        Assert.Equal(teacher.Address, savedParticipant.Envelope.Address);
        Assert.Equal(ParticipantKind.Human, savedParticipant.Envelope.Kind);

        // Conversation + membership round-trip.
        var savedConversation = db.Conversations
            .Include(c => c.Participants)
            .Single(c => c.Id == conversation.Id);
        Assert.Equal("Assessment", savedConversation.Title);
        Assert.Equal(conversation.CreatedAt, savedConversation.CreatedAt);

        var membership = Assert.Single(savedConversation.Participants);
        Assert.Equal(teacher.Address, membership.ParticipantAddress);
        Assert.Equal(conversation.Id, membership.ConversationId);
    }

    [Fact]
    public void Message_RoundTrip_CanonicalJsonPreserved()
    {
        using var db = CreateContext();
        db.Database.Migrate();

        var message = TestData.NewMessage();
        db.Messages.Add(MessageRecord.FromEnvelope(message));
        db.SaveChanges();

        var saved = db.Messages.Single(m => m.Id == message.Id);
        Assert.Equal(message.ConversationId, saved.ConversationId);
        Assert.Equal(message.Sender.Address, saved.SenderAddress);
        Assert.Equal(message.ContentHash, saved.ContentHash);

        // The stored envelope re-serialises to the exact canonical wire JSON (EDGE-FR-02).
        var originalJson = JsonSerializer.Serialize(message, ProtocolJson.Options);
        var roundTripJson = JsonSerializer.Serialize(saved.Envelope, ProtocolJson.Options);
        Assert.Equal(originalJson, roundTripJson);
    }

    [Fact]
    public void Delivery_RoundTrip_StateTokenDenormalised()
    {
        using var db = CreateContext();
        db.Database.Migrate();

        var delivery = TestData.NewDelivery("message:" + IdGenerator.NewId(), TestData.Teacher, DeliveryState.Queued);
        db.Deliveries.Add(DeliveryRecord.FromEnvelope(delivery));
        db.SaveChanges();

        var saved = db.Deliveries.Single(d => d.Id == delivery.Id);
        Assert.Equal("QUEUED", saved.State);
        Assert.Equal(delivery.MessageId, saved.MessageId);
        Assert.Equal(TestData.Teacher.Address, saved.RecipientAddress);
        Assert.Equal(DeliveryState.Queued, saved.Envelope.State);
    }

    [Fact]
    public void Delivery_UniqueIndex_RejectsDuplicateMessageRecipient()
    {
        using var db = CreateContext();
        db.Database.Migrate();

        var messageId = "message:" + IdGenerator.NewId();
        db.Deliveries.Add(DeliveryRecord.FromEnvelope(TestData.NewDelivery(messageId, TestData.Teacher)));
        db.SaveChanges();

        // A second delivery for the same (message, recipient) — even with a distinct record ID — must be
        // rejected by the unique index (PROTO-FR-05, one delivery per recipient per message).
        db.Deliveries.Add(DeliveryRecord.FromEnvelope(TestData.NewDelivery(messageId, TestData.Teacher)));
        var ex = Assert.Throws<DbUpdateException>(() => db.SaveChanges());

        var detail = ex.InnerException?.Message ?? ex.Message;
        Assert.Contains("UNIQUE constraint failed", detail);
    }

    [Fact]
    public void Artifact_RoundTrip_DedupColumnsPreserved()
    {
        using var db = CreateContext();
        db.Database.Migrate();

        var artifact = TestData.NewArtifact();
        db.Artifacts.Add(ArtifactRecord.FromEnvelope(artifact));
        db.SaveChanges();

        var saved = db.Artifacts.Single(a => a.Id == artifact.Id);
        Assert.Equal(artifact.Hash, saved.Hash);
        Assert.Equal(artifact.SizeBytes, saved.SizeBytes);
        Assert.Equal("application/pdf", saved.MimeType);
        Assert.Equal("evidence.pdf", saved.Envelope.Filename);
    }

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }
}
