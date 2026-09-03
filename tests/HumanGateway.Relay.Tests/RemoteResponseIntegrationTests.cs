extern alias edge;

using System.Net.Http.Json;
using System.Text.Json;
using HumanGateway.Core.Artifacts;
using HumanGateway.Core.Hashing;
using HumanGateway.Core.Idempotency;
using HumanGateway.Core.Ids;
using HumanGateway.Core.Inbox;
using HumanGateway.Core.Outbox;
using HumanGateway.Core.Sync;
using EdgeApi = edge::HumanGateway.Edge.Api;
using EdgeArtifacts = edge::HumanGateway.Edge.Artifacts;
using EdgeStorage = edge::HumanGateway.Edge.Storage;
using EdgeSync = edge::HumanGateway.Edge.Sync;
using HumanGateway.Protocol;
using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HumanGateway.Relay.Tests;

/// <summary>
/// Full response path through the Relay: a school Edge publishes a task, a remote Edge pulls and answers it,
/// and the school Edge pulls the response into its local task store. This deliberately uses the production sync
/// worker and inbound projector with only the HTTP transport supplied by the test, so the workflow handoff is
/// verified at the durable HumanTask boundary.
/// </summary>
public sealed class RemoteResponseIntegrationTests : IClassFixture<PostgresRelayFixture>
{
    private static readonly JsonSerializerOptions WireJson = CreateWireJson();
    private readonly PostgresRelayFixture _fixture;

    public RemoteResponseIntegrationTests(PostgresRelayFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RemoteUserResponse_ReachesSchoolEdge_WithWorkflowCorrelation()
    {
        using var relayFactory = new RelayApiFactory(_fixture);
        using var relayClient = relayFactory.CreateClient();
        var schoolId = UniqueGatewayId("gateway:school-response");
        var remoteId = UniqueGatewayId("gateway:remote-response");

        var schoolToken = await RegisterAsync(relayClient, schoolId);
        var remoteToken = await RegisterAsync(relayClient, remoteId);

        using var schoolHttp = relayFactory.CreateDefaultClient(SignedHandler(schoolId, () => schoolToken));
        using var remoteHttp = relayFactory.CreateDefaultClient(SignedHandler(remoteId, () => remoteToken));
        using var school = await NewEdge(schoolId, schoolHttp, projectInbound: true);
        using var remote = await NewEdge(remoteId, remoteHttp, projectInbound: false);

        var remoteUser = new Participant
        {
            Address = "human:remote@example.org",
            Kind = ParticipantKind.Human,
            DisplayName = "Remote User",
            GatewayId = remoteId,
        };
        var schoolAgent = new Participant
        {
            Address = "agent:workflow@school.example",
            Kind = ParticipantKind.Agent,
            DisplayName = "School Workflow",
            GatewayId = schoolId,
        };
        const string workflowRef = "workflow:remote-assessment";
        const string correlationToken = "consumer-correlation-42";

        var taskId = IdGenerator.NewId();
        var requestMessage = TaskMessage(
            IdGenerator.NewId(), schoolAgent, remoteUser,
            new HumanTask
            {
                Id = taskId,
                Kind = HumanTaskKind.Input,
                Status = HumanTaskStatus.Requested,
                WorkflowRef = workflowRef,
                NodeId = "node:question",
                Role = "teacher",
                Prompt = "What is 2 + 2?",
                Subject = "Assessment",
                RequestMessageId = "pending-request",
                CorrelationToken = correlationToken,
                RequestedAt = "2026-09-01T12:00:00.000Z",
                CreatedAt = "2026-09-01T12:00:00.000Z",
            });
        var requestTask = JsonSerializer.Deserialize<HumanTask>(
            requestMessage.Payload.Data!.Value.GetProperty("humanTask").GetRawText(), ProtocolJson.Options)!;
        requestMessage = requestMessage with
        {
            Payload = requestMessage.Payload with
            {
                Data = JsonSerializer.SerializeToElement(
                    new { humanTask = requestTask with { RequestMessageId = requestMessage.Id } }, ProtocolJson.Options),
            },
        };
        requestMessage = requestMessage with { ContentHash = ContentHasher.ComputeMessageHash(requestMessage) };
        await school.Outbox.EnqueueAsync(schoolId, MessageItem(requestMessage));

        await school.Worker.SyncOnceAsync();
        await remote.Worker.SyncOnceAsync();

        // The remote worker's pull above is the actual delivery; inspect its durable inbox through the response
        // batch so the test proves the recipient received the request before answering it.
        Assert.True(remote.LastPulledItems.Count == 1);
        var deliveredMessage = remote.LastPulledItems[0];
        var deliveredTask = JsonSerializer.Deserialize<HumanTask>(
            deliveredMessage.Payload.Data!.Value.GetProperty("humanTask").GetRawText(), ProtocolJson.Options)!;
        Assert.Equal(taskId, deliveredTask.Id);
        Assert.Equal(workflowRef, deliveredTask.WorkflowRef);
        Assert.Equal(correlationToken, deliveredTask.CorrelationToken);

        var responseId = IdGenerator.NewId();
        var responseTask = deliveredTask with
        {
            Status = HumanTaskStatus.ResponseReceived,
            ResponseMessageId = responseId,
            Response = new TaskResponse
            {
                Text = "4",
                RespondedBy = remoteUser,
                RespondedAt = "2026-09-01T12:01:00.000Z",
            },
            ResponseReceivedAt = "2026-09-01T12:01:00.000Z",
            UpdatedAt = "2026-09-01T12:01:00.000Z",
        };
        var responseMessage = TaskMessage(responseId, remoteUser, schoolAgent, responseTask) with
        {
            ReplyToMessageId = deliveredMessage.Id,
            HumanTaskId = taskId,
            WorkflowRef = workflowRef,
            CorrelationTokens = new Dictionary<string, string> { ["correlationToken"] = correlationToken },
        };
        responseMessage = responseMessage with { ContentHash = ContentHasher.ComputeMessageHash(responseMessage) };
        await remote.Outbox.EnqueueAsync(remoteId, MessageItem(responseMessage));

        await remote.Worker.SyncOnceAsync();
        await school.Worker.SyncOnceAsync();

        await using var db = school.Factory.CreateDbContext();
        Assert.True(await db.Messages.AnyAsync(m => m.Id == responseId));
        var persisted = await db.Tasks.SingleAsync(t => t.Id == taskId);
        Assert.Equal(workflowRef, persisted.Envelope.WorkflowRef);
        Assert.Equal(correlationToken, persisted.Envelope.CorrelationToken);
        Assert.Equal(HumanTaskStatus.ResponseReceived, persisted.Envelope.Status);
        Assert.Equal(responseId, persisted.Envelope.ResponseMessageId);
        Assert.Equal("4", persisted.Envelope.Response!.Text);
        Assert.Equal(remoteUser.Address, persisted.Envelope.Response.RespondedBy!.Address);
    }

    private static Message TaskMessage(string id, Participant sender, Participant recipient, HumanTask task)
        => new()
        {
            Id = id,
            Sender = sender,
            Recipients = new List<Participant> { recipient },
            ConversationId = "conversation:remote-response",
            WorkflowRef = task.WorkflowRef,
            HumanTaskId = task.Id,
            Payload = new MessagePayload
            {
                Body = task.Response is null ? task.Prompt : task.Response.Text ?? string.Empty,
                Format = MessageFormat.Plaintext,
                Data = JsonSerializer.SerializeToElement(new { humanTask = task }, ProtocolJson.Options),
            },
            CorrelationTokens = task.CorrelationToken is null
                ? null
                : new Dictionary<string, string> { ["correlationToken"] = task.CorrelationToken },
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            ContentHash = "pending",
        };

    private static SyncItem MessageItem(Message message) => new()
    {
        Kind = SyncItemKind.Message,
        Sequence = 0,
        Message = message,
    };

    private async Task<string> RegisterAsync(HttpClient client, string gatewayId)
    {
        var issued = await client.PostAsJsonAsync("/gateways", new { gatewayId }, WireJson);
        issued.EnsureSuccessStatusCode();
        var token = (await issued.Content.ReadFromJsonAsync<RegistrationIssued>(WireJson))!.RegistrationToken;
        var confirm = await client.PostAsJsonAsync($"/gateways/{gatewayId}/register",
            new { gatewayId, registrationToken = token }, WireJson);
        confirm.EnsureSuccessStatusCode();
        return token;
    }

    private static edge::HumanGateway.Edge.Security.SignedGatewayRequestHandler SignedHandler(
        string gatewayId, Func<string?> token)
        => new(gatewayId, token,
            NullLogger<edge::HumanGateway.Edge.Security.SignedGatewayRequestHandler>.Instance);

    private static async Task<EdgeHarness> NewEdge(string gatewayId, HttpClient http, bool projectInbound)
    {
        var dir = Path.Combine(Path.GetTempPath(), "hgedge-response-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dir, "edge.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
        var options = new DbContextOptionsBuilder<EdgeStorage.EdgeDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new EdgeStorage.SqlitePragmaInterceptor())
            .Options;
        var factory = new Microsoft.EntityFrameworkCore.Infrastructure.PooledDbContextFactory<EdgeStorage.EdgeDbContext>(options);
        await using (var db = factory.CreateDbContext()) await db.Database.MigrateAsync();

        var outbox = new EdgeStorage.SqliteOutbox(factory);
        var inbox = new EdgeStorage.SqliteInbox(factory);
        var engine = new SyncEngine(outbox, inbox, new EdgeStorage.SqliteIdempotencyStore(factory));
        var client = new HttpRelaySyncClient(http, gatewayId);
        var worker = new EdgeSync.SyncWorker(
            engine, outbox, new EdgeStorage.SqliteSyncCursorStore(factory), client,
            new EdgeArtifacts.FilesystemArtifactStore(Path.Combine(dir, "artifacts")),
            new NoopArtifactTransfer(), Microsoft.Extensions.Options.Options.Create(new EdgeApi.GatewayOptions { GatewayId = gatewayId }),
            Microsoft.Extensions.Options.Options.Create(new EdgeSync.SyncWorkerOptions()), NullLogger<EdgeSync.SyncWorker>.Instance,
            inboundMessages: projectInbound ? new EdgeSync.InboundMessageProjector(factory) : null);
        return new EdgeHarness(dir, factory, outbox, worker, client, http);
    }

    private static JsonSerializerOptions CreateWireJson()
    {
        var options = new JsonSerializerOptions();
        RelayJson.Configure(options);
        return options;
    }

    private static string UniqueGatewayId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private sealed class EdgeHarness : IDisposable
    {
        private readonly string _dir;
        public EdgeHarness(string dir, IDbContextFactory<EdgeStorage.EdgeDbContext> factory, IOutbox outbox,
            EdgeSync.SyncWorker worker, HttpRelaySyncClient client, HttpClient http)
            => (_dir, Factory, Outbox, Worker, Client, Http) = (dir, factory, outbox, worker, client, http);
        public IDbContextFactory<EdgeStorage.EdgeDbContext> Factory { get; }
        public IOutbox Outbox { get; }
        public EdgeSync.SyncWorker Worker { get; }
        public HttpRelaySyncClient Client { get; }
        public HttpClient Http { get; }
        public List<Message> LastPulledItems => Client.LastPulledItems;
        public void Dispose() { Http.Dispose(); Directory.Delete(_dir, true); }
    }

    private sealed class HttpRelaySyncClient : EdgeSync.IRelaySyncClient
    {
        private readonly HttpClient _http;
        private readonly string _gatewayId;
        public HttpRelaySyncClient(HttpClient http, string gatewayId) => (_http, _gatewayId) = (http, gatewayId);
        public bool IsConfigured => true;
        public List<Message> LastPulledItems { get; } = new();
        public async Task<EdgeSync.PushResult> PushAsync(SyncBatch batch, CancellationToken ct = default)
        {
            var response = await _http.PostAsJsonAsync("/sync/push", batch, WireJson, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SyncBatch>(WireJson, ct);
            return new EdgeSync.PushResult { Cursor = result?.Cursor };
        }
        public async Task<SyncBatch?> PullAsync(string? sinceCursor, CancellationToken ct = default)
        {
            var response = await _http.PostAsJsonAsync("/sync/pull",
                new { gatewayId = _gatewayId, sinceCursor }, WireJson, ct);
            response.EnsureSuccessStatusCode();
            var batch = await response.Content.ReadFromJsonAsync<SyncBatch>(WireJson, ct);
            LastPulledItems.Clear();
            foreach (var item in batch?.Items ?? new List<SyncItem>())
                if (item.Message is not null) LastPulledItems.Add(item.Message);
            return batch;
        }
    }

    private sealed class NoopArtifactTransfer : IArtifactTransfer
    {
        public bool IsConfigured => true;
        public Task<IReadOnlyList<string>> CheckHashesAsync(IReadOnlyCollection<string> hashes, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task UploadAsync(string hash, long sizeBytes, Stream content, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long?> GetRemoteSizeAsync(string hash, CancellationToken ct = default) => Task.FromResult<long?>(null);
        public Task<long> DownloadAsync(string hash, Stream sink, CancellationToken ct = default) => Task.FromResult(0L);
    }
}
