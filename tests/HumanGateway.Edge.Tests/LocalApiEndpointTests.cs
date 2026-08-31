using System.Net;
using System.Net.Http.Headers;
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

        /// <summary>The seeded bootstrap credentials used by the tests.</summary>
        public (string Username, string Password) Bootstrap { get; } = ("bootstrap", "bootstrap-pw");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_dir, "edge.db"),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString();

            // UseSetting writes a HOST setting, available to builder.Configuration from the very start of
            // Program.cs. A plain ConfigureAppConfiguration override is applied only at builder.Build(), so
            // Program.cs's `GetConnectionString("Edge")` would silently fall back to the shared repo default
            // database instead of this test's temp file (same precedence trap as the Relay factory, CLOUD-RELAY-4.3).
            builder.UseSetting("ConnectionStrings:Edge", connectionString);

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Artifacts:RootPath"] = Path.Combine(_dir, "artifacts"),
                    ["Auth:BootstrapUser:Username"] = Bootstrap.Username,
                    ["Auth:BootstrapUser:Password"] = Bootstrap.Password,
                    ["Auth:BootstrapUser:DisplayName"] = "Bootstrap User",
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
        // The local API is session-gated (AUTH-FR-03, SP-04): log in first and attach the bearer token.
        var client = await CreateAuthenticatedClientAsync();

        // Resolve the authenticated user's id and link the teacher participant to it (the local participant
        // directory maps participant.userId → user account, which is what membership authz resolves).
        var me = await client.GetAsync("/auth/me");
        var myUserId = JsonDocument.Parse(await me.Content.ReadAsStringAsync()).RootElement.GetProperty("userId").GetString();
        var teacher = new { address = "human:teacher@school.example", kind = "human", displayName = "Teacher", userId = myUserId };

        // 1. Create a conversation (upserts its participants).
        var createConversation = await client.PostAsync("/conversations", JsonBody(new
        {
            title = "Assessment",
            participants = new[]
            {
                teacher,
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

            // 2. List conversations includes the new one (filtered to the user's membership).
            var list = await client.GetAsync("/conversations");
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var listDoc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            Assert.Contains(listDoc.RootElement.EnumerateArray(), c => c.GetProperty("id").GetString() == conversationId);

            // 3. Send a message: durable store + per-recipient delivery + outbox enqueue.
            var send = await client.PostAsync("/messages", JsonBody(new
            {
                sender = teacher,
                recipients = new[] { new { address = "agent:assistant@school.example", kind = "agent", displayName = "Assistant" } },
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
                assignees = new[] { teacher },
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
                respondedBy = teacher,
                text = "4",
            }));

            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
            using var answeredDoc = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());
            Assert.Equal("RESPONSE_RECEIVED", answeredDoc.RootElement.GetProperty("status").GetString());
            Assert.Equal("4", answeredDoc.RootElement.GetProperty("response").GetProperty("text").GetString());

            // 8. Register an artifact and read it back. Bytes land later; the artifact is only visible to
            //    members of a conversation that references it (AUTH-FR-05), so attach it to a message.
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

            var attach = await client.PostAsync("/messages", JsonBody(new
            {
                sender = teacher,
                recipients = new[] { new { address = "agent:assistant@school.example", kind = "agent", displayName = "Assistant" } },
                conversationId,
                payload = new { body = "evidence attached" },
                artifactRefs = new[]
                {
                    new { id = artifactId, hash = "sha256:0000000000000000000000000000000000000000000000000000000000000000", filename = "evidence.pdf", mimeType = "application/pdf", sizeBytes = 12 },
                },
            }));
            Assert.Equal(HttpStatusCode.Created, attach.StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/artifacts")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/artifacts/{artifactId}")).StatusCode);

            // 9. Sync status reflects the durable side effects: four messages (send + task request + task
            //    response + artifact attach) enqueued to the outbox with a QUEUED delivery each.
            var syncStatus = await client.GetAsync("/sync/status");
            Assert.Equal(HttpStatusCode.OK, syncStatus.StatusCode);
            using var syncDoc = JsonDocument.Parse(await syncStatus.Content.ReadAsStringAsync());
            Assert.Equal("edge:scaffold", syncDoc.RootElement.GetProperty("gatewayId").GetString());
            Assert.True(syncDoc.RootElement.GetProperty("queued").GetInt32() >= 4);
            Assert.True(syncDoc.RootElement.GetProperty("deliveries").GetProperty("queued").GetInt32() >= 4);
        }
    }

    // -----------------------------------------------------------------------------------------------
    // Error contract (ProtocolError shape, SP-07)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task UnknownConversation_IsRejectedAsForbidden_WithoutLeakingExistence()
    {
        var client = await CreateAuthenticatedClientAsync();

        // A non-existent conversation is indistinguishable from an inaccessible one: 403, never 404
        // (SP-07 — resource existence cannot be probed).
        var response = await client.GetAsync("/conversations/conversation:does-not-exist");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CONVERSATION_ACCESS_DENIED", doc.RootElement.GetProperty("code").GetString());
        Assert.False(doc.RootElement.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task ProtectedRoute_WithoutSession_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/conversations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("UNAUTHORIZED", doc.RootElement.GetProperty("code").GetString());
        Assert.False(doc.RootElement.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task SendMessage_NoRecipients_ReturnsValidationProtocolError()
    {
        var client = await CreateAuthenticatedClientAsync();
        var me = await client.GetAsync("/auth/me");
        var myUserId = JsonDocument.Parse(await me.Content.ReadAsStringAsync()).RootElement.GetProperty("userId").GetString();

        var createConversation = await client.PostAsync("/conversations", JsonBody(new
        {
            participants = new[]
            {
                new { address = "human:teacher@school.example", kind = "human", displayName = "Teacher", userId = myUserId },
            },
        }));
        using var conversationDoc = JsonDocument.Parse(await createConversation.Content.ReadAsStringAsync());
        var conversationId = conversationDoc.RootElement.GetProperty("id").GetString();

        var send = await client.PostAsync("/messages", JsonBody(new
        {
            sender = new { address = "human:teacher@school.example", kind = "human", displayName = "Teacher", userId = myUserId },
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

    /// <summary>Creates a client logged in as the seeded bootstrap user with the bearer token attached.</summary>
    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsync(client, _factory.Bootstrap.Username, _factory.Bootstrap.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsync("/auth/login", JsonBody(new { username, password }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    private static StringContent JsonBody(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}
