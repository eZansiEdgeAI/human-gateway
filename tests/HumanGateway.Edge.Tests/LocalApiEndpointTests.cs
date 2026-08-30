using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HumanGateway.Edge.Tests;

/// <summary>
/// HTTP-level integration tests for the Edge local REST API (LOCAL-EDGE-1.4, EDGE-FR-03): boots the real
/// <c>Program</c> via <c>WebApplicationFactory&lt;Program&gt;</c> over a throwaway temp SQLite file and exercises
/// the full endpoint surface — conversations, messages, tasks, artifacts, and sync status — over the actual
/// wire. This proves acceptance criterion §7 #3 ("the Edge exposes a local REST API consumable by the PWA"):
/// exact camelCase keys, exact string enum tokens, <c>ProtocolError</c>-shaped failures, and durable-write
/// side effects (outbox + delivery counts) visible through <c>/sync/status</c>.
/// </summary>
public sealed class LocalApiEndpointTests : IClassFixture<LocalApiEndpointTests.Factory>
{
    private readonly Factory _factory;

    public LocalApiEndpointTests(Factory factory) => _factory = factory;

    /// <summary>
    /// Hosts the Edge over a unique temp SQLite database by overriding the connection string in configuration.
    /// Program.cs migrates on startup, so the first request materialises the full schema (WAL + synchronous=NORMAL)
    /// in the temp file.
    /// </summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "hgedge-http-" + Guid.NewGuid().ToString("N"));

        public Factory() => Directory.CreateDirectory(_dir);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_dir, "edge.db"),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString();

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Edge"] = connectionString,
                    ["Artifacts:RootPath"] = Path.Combine(_dir, "artifacts"),
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
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
    }

    // -----------------------------------------------------------------------------------------------
    // Health probe
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task HealthProbe_ReturnsOkWithStore()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("sqlite", doc.RootElement.GetProperty("store").GetString());
    }

    // -----------------------------------------------------------------------------------------------
    // End-to-end contract: conversations → messages → tasks → artifacts → sync status
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task EndToEnd_ConversationMessageTaskArtifactSyncStatus_OverHttp()
    {
        var client = _factory.CreateClient();

        // 1. Create a conversation (upserts its participants).
        var createConversation = await client.PostAsync("/conversations", JsonBody(new
        {
            title = "Assessment",
            participants = new[]
            {
                new { address = "human:teacher@school.example", kind = "human", displayName = "Teacher", userId = (string?)"user:teacher" },
                new { address = "agent:assistant@school.example", kind = "agent", displayName = "Assistant", userId = (string?)null },
            },
        }));

        Assert.Equal(HttpStatusCode.Created, createConversation.StatusCode);
        Assert.StartsWith("/conversations/", createConversation.Headers.Location?.ToString() ?? string.Empty, StringComparison.Ordinal);

        using (var conversationDoc = JsonDocument.Parse(await createConversation.Content.ReadAsStringAsync()))
        {
            var root = conversationDoc.RootElement;
            var conversationId = root.GetProperty("id").GetString();
            Assert.False(string.IsNullOrEmpty(conversationId));
            Assert.Equal("Assessment", root.GetProperty("title").GetString());
            Assert.Equal(2, root.GetProperty("participants").GetArrayLength());
            // Exact wire enum tokens (lowercase kind) — participants are ordered by address, so assert as a set.
            var kinds = root.GetProperty("participants").EnumerateArray()
                .Select(p => p.GetProperty("kind").GetString())
                .OrderBy(k => k)
                .ToArray();
            Assert.Equal(new[] { "agent", "human" }, kinds);

            // 2. List conversations includes the new one.
            var list = await client.GetAsync("/conversations");
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var listDoc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            Assert.Contains(listDoc.RootElement.EnumerateArray(), c => c.GetProperty("id").GetString() == conversationId);

            // 3. Send a message: durable store + per-recipient delivery + outbox enqueue.
            var send = await client.PostAsync("/messages", JsonBody(new
            {
                sender = new { address = "agent:assistant@school.example", kind = "agent", displayName = "Assistant" },
                recipients = new[] { new { address = "human:teacher@school.example", kind = "human", displayName = "Teacher" } },
                conversationId,
                payload = new { body = "hello", format = "plaintext" },
            }));

            Assert.Equal(HttpStatusCode.Created, send.StatusCode);
            using var messageDoc = JsonDocument.Parse(await send.Content.ReadAsStringAsync());
            var messageId = messageDoc.RootElement.GetProperty("message").GetProperty("id").GetString();
            Assert.StartsWith("sha256:", messageDoc.RootElement.GetProperty("message").GetProperty("contentHash").GetString());
            Assert.Equal("QUEUED", messageDoc.RootElement.GetProperty("deliveries")[0].GetProperty("state").GetString());

            // 4. Fetch the message and the conversation's message list.
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/messages/{messageId}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/conversations/{conversationId}/messages")).StatusCode);

            // 5. Create a human task (input).
            var createTask = await client.PostAsync("/tasks", JsonBody(new
            {
                kind = "input",
                workflowRef = "workflow:assessment-1",
                nodeId = "node-input",
                prompt = "What is 2 + 2?",
                requester = new { address = "agent:assistant@school.example", kind = "agent", displayName = "Assistant" },
                assignees = new[] { new { address = "human:teacher@school.example", kind = "human", displayName = "Teacher" } },
            }));

            Assert.Equal(HttpStatusCode.Created, createTask.StatusCode);
            using var taskDoc = JsonDocument.Parse(await createTask.Content.ReadAsStringAsync());
            var taskId = taskDoc.RootElement.GetProperty("id").GetString();
            Assert.Equal("REQUESTED", taskDoc.RootElement.GetProperty("status").GetString());
            Assert.Equal("workflow:assessment-1", taskDoc.RootElement.GetProperty("workflowRef").GetString());

            // 6. List tasks (filtered) and fetch the task.
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tasks?status=REQUESTED")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/tasks/{taskId}")).StatusCode);

            // 7. Answer the task → RESPONSE_RECEIVED.
            var answer = await client.PostAsync($"/tasks/{taskId}/response", JsonBody(new
            {
                respondedBy = new { address = "human:teacher@school.example", kind = "human", displayName = "Teacher" },
                text = "4",
            }));

            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
            using var answeredDoc = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());
            Assert.Equal("RESPONSE_RECEIVED", answeredDoc.RootElement.GetProperty("status").GetString());
            Assert.Equal("4", answeredDoc.RootElement.GetProperty("response").GetProperty("text").GetString());

            // 8. Register an artifact and read it back.
            var registerArtifact = await client.PostAsync("/artifacts", JsonBody(new
            {
                hash = "sha256:0000000000000000000000000000000000000000000000000000000000000000",
                sizeBytes = 12,
                mimeType = "application/pdf",
                filename = "evidence.pdf",
            }));

            Assert.Equal(HttpStatusCode.Created, registerArtifact.StatusCode);
            using var artifactDoc = JsonDocument.Parse(await registerArtifact.Content.ReadAsStringAsync());
            var artifactId = artifactDoc.RootElement.GetProperty("id").GetString();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/artifacts")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/artifacts/{artifactId}")).StatusCode);

            // 9. Sync status reflects the durable side effects: three messages (send + task request + task
            //    response) enqueued to the outbox with a QUEUED delivery each.
            var syncStatus = await client.GetAsync("/sync/status");
            Assert.Equal(HttpStatusCode.OK, syncStatus.StatusCode);
            using var syncDoc = JsonDocument.Parse(await syncStatus.Content.ReadAsStringAsync());
            Assert.Equal("edge:scaffold", syncDoc.RootElement.GetProperty("gatewayId").GetString());
            Assert.True(syncDoc.RootElement.GetProperty("queued").GetInt32() >= 3);
            Assert.True(syncDoc.RootElement.GetProperty("deliveries").GetProperty("queued").GetInt32() >= 3);
        }
    }

    // -----------------------------------------------------------------------------------------------
    // Error contract (ProtocolError shape, SP-07)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task UnknownConversation_ReturnsNotFoundProtocolError()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/conversations/conversation:does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("NOT_FOUND", doc.RootElement.GetProperty("code").GetString());
        Assert.False(doc.RootElement.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task SendMessage_NoRecipients_ReturnsValidationProtocolError()
    {
        var client = _factory.CreateClient();

        var createConversation = await client.PostAsync("/conversations", JsonBody(new
        {
            participants = new[]
            {
                new { address = "human:teacher@school.example", kind = "human", displayName = "Teacher" },
            },
        }));
        using var conversationDoc = JsonDocument.Parse(await createConversation.Content.ReadAsStringAsync());
        var conversationId = conversationDoc.RootElement.GetProperty("id").GetString();

        var send = await client.PostAsync("/messages", JsonBody(new
        {
            sender = new { address = "agent:assistant@school.example", kind = "agent", displayName = "Assistant" },
            recipients = Array.Empty<object>(),
            conversationId,
            payload = new { body = "nobody home" },
        }));

        Assert.Equal(HttpStatusCode.BadRequest, send.StatusCode);
        using var doc = JsonDocument.Parse(await send.Content.ReadAsStringAsync());
        Assert.Equal("VALIDATION_FAILED", doc.RootElement.GetProperty("code").GetString());
        Assert.False(doc.RootElement.GetProperty("retryable").GetBoolean());
        // Details carry the machine-readable validation errors (path + code), not a bare string.
        Assert.True(doc.RootElement.GetProperty("details").GetArrayLength() >= 1);
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    private static StringContent JsonBody(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}
