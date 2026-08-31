using HumanGateway.Edge.Api;
using HumanGateway.Edge.Artifacts;
using HumanGateway.Edge.Storage;
using HumanGateway.Protocol.Models;
using HumanGateway.Protocol.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace HumanGateway.Edge.Tests;

/// <summary>
/// Local REST API service tests (LOCAL-EDGE-1.4, EDGE-FR-03): exercises the <see cref="LocalApiService"/>
/// domain operations the endpoint layer fronts — conversations, messages, tasks, artifacts, and sync status —
/// over a real temp SQLite store, verifying the durable-write + outbox-enqueue behaviour the PWA depends on
/// (PWA-FR-02/04/05/06) and the delivery lifecycle's QUEUED starting state.
/// </summary>
public sealed class LocalApiTests : IDisposable
{
    private const string GatewayId = "edge:00000000-0000-0000-0000-00000000test";
    private static readonly CancellationToken Ct = CancellationToken.None;

    private readonly string _dir;
    private readonly string _dbPath;

    public LocalApiTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "hglocalapi-tests-" + Guid.NewGuid().ToString("N"));
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

    private LocalApiService CreateService()
    {
        var options = new DbContextOptionsBuilder<EdgeDbContext>()
            .UseSqlite(TestConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
        var factory = new PooledDbContextFactory<EdgeDbContext>(options);

        using (var db = factory.CreateDbContext())
        {
            db.Database.Migrate();
        }

        var gatewayOptions = Options.Create(new GatewayOptions { GatewayId = GatewayId });
        var artifactStore = new FilesystemArtifactStore(Path.Combine(_dbPath, "artifacts"));
        var artifactOptions = Options.Create(new ArtifactStoreOptions());
        return new LocalApiService(factory, gatewayOptions, artifactStore, artifactOptions);
    }

    // -----------------------------------------------------------------------------------------------
    // Conversations
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task CreateConversation_IsListable_AndGetReturnsMembership()
    {
        var service = CreateService();

        var created = await service.CreateConversationAsync(new CreateConversationRequest
        {
            Title = "Assessment",
            Participants = new List<Participant> { TestData.Teacher, TestData.Assistant },
        }, Ct);

        Assert.False(string.IsNullOrEmpty(created.Id));
        Assert.Equal("Assessment", created.Title);
        Assert.Equal(2, created.Participants.Count);
        Assert.Equal(0, created.MessageCount);

        var listed = await service.ListConversationsAsync(Ct);
        Assert.Contains(listed, c => c.Id == created.Id);

        var fetched = await service.GetConversationAsync(created.Id, Ct);
        Assert.NotNull(fetched);
        Assert.Equal("Assessment", fetched!.Title);
        Assert.Equal(2, fetched.Participants.Count);
    }

    [Fact]
    public async Task CreateConversation_WithNoParticipants_ThrowsBadRequest()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<LocalApiException>(() =>
            service.CreateConversationAsync(new CreateConversationRequest { Title = "Empty" }, Ct));

        Assert.Equal(400, ex.StatusCode);
    }

    // -----------------------------------------------------------------------------------------------
    // Messages
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task SendMessage_StoresMessageQueuedDeliveryAndOutboxEntry()
    {
        var service = CreateService();

        var conversation = await service.CreateConversationAsync(new CreateConversationRequest
        {
            Title = "Math",
            Participants = new List<Participant> { TestData.Teacher, TestData.Assistant },
        }, Ct);

        var sent = await service.SendMessageAsync(new SendMessageRequest
        {
            Sender = TestData.Assistant,
            Recipients = new List<Participant> { TestData.Teacher },
            ConversationId = conversation.Id,
            Payload = new MessagePayload { Body = "hello", Format = MessageFormat.Plaintext },
        }, Ct);

        // Message envelope round-trips with a computed content hash.
        Assert.False(string.IsNullOrEmpty(sent.Message.Id));
        Assert.Equal("hello", sent.Message.Payload.Body);
        Assert.StartsWith("sha256:", sent.Message.ContentHash);

        // One delivery per recipient, starting at QUEUED (the PWA status indicator's initial state).
        var delivery = Assert.Single(sent.Deliveries);
        Assert.Equal(DeliveryState.Queued, delivery.State);
        Assert.Equal(TestData.Teacher.Address, delivery.Recipient.Address);
        Assert.Equal(sent.Message.Id, delivery.MessageId);

        // The message is durable and shows up in the conversation's message list.
        var messages = await service.ListConversationMessagesAsync(conversation.Id, Ct);
        var listed = Assert.Single(messages);
        Assert.Equal(sent.Message.Id, listed.Message.Id);
        Assert.Single(listed.Deliveries);

        // The durable outbox holds exactly one pending message item (PWA-FR-02 / EDGE-FR-04).
        var status = await service.GetSyncStatusAsync(Ct);
        Assert.Equal(1, status.Queued);
        Assert.Equal(1, status.Deliveries.Queued);
    }

    [Fact]
    public async Task SendMessage_InvalidMessage_ThrowsValidation()
    {
        var service = CreateService();

        var conversation = await service.CreateConversationAsync(new CreateConversationRequest
        {
            Participants = new List<Participant> { TestData.Teacher, TestData.Assistant },
        }, Ct);

        // No recipients → message.schema.json requires at least one recipient.
        await Assert.ThrowsAsync<ProtocolValidationException>(() => service.SendMessageAsync(new SendMessageRequest
        {
            Sender = TestData.Assistant,
            Recipients = new List<Participant>(),
            ConversationId = conversation.Id,
            Payload = new MessagePayload { Body = "nobody home" },
        }, Ct));
    }

    [Fact]
    public async Task GetMessage_UnknownId_ReturnsNull()
    {
        var service = CreateService();

        var message = await service.GetMessageAsync("message:does-not-exist", Ct);
        Assert.Null(message);
    }

    // -----------------------------------------------------------------------------------------------
    // Tasks
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task CreateInputTask_ThenAnswer_TransitionsToResponseReceived()
    {
        var service = CreateService();

        var task = await service.CreateTaskAsync(new CreateTaskRequest
        {
            Kind = HumanTaskKind.Input,
            WorkflowRef = "workflow:assessment-1",
            NodeId = "node-input",
            Prompt = "What is 2 + 2?",
            Requester = TestData.Assistant,
            Assignees = new List<Participant> { TestData.Teacher },
        }, Ct);

        Assert.Equal(HumanTaskStatus.Requested, task.Status);
        Assert.Equal("workflow:assessment-1", task.WorkflowRef);
        Assert.False(string.IsNullOrEmpty(task.RequestMessageId));

        // The request message was durably stored and enqueued to the outbox.
        Assert.NotNull(await service.GetMessageAsync(task.RequestMessageId, Ct));
        Assert.Equal(1, (await service.GetSyncStatusAsync(Ct)).Queued);

        var answered = await service.AnswerTaskAsync(task.Id, new AnswerTaskRequest
        {
            RespondedBy = TestData.Teacher,
            Text = "4",
        }, Ct);

        Assert.NotNull(answered);
        Assert.Equal(HumanTaskStatus.ResponseReceived, answered!.Status);
        Assert.Equal("4", answered.Response!.Text);
        Assert.False(string.IsNullOrEmpty(answered.ResponseMessageId));

        // The response message is also durable, and the outbox now carries both the request and the response.
        Assert.NotNull(await service.GetMessageAsync(answered.ResponseMessageId!, Ct));
        Assert.Equal(2, (await service.GetSyncStatusAsync(Ct)).Queued);
    }

    [Fact]
    public async Task CreateApprovalTask_AnswerWithoutDecision_ThrowsValidation()
    {
        var service = CreateService();

        var task = await service.CreateTaskAsync(new CreateTaskRequest
        {
            Kind = HumanTaskKind.Approval,
            WorkflowRef = "workflow:approval-1",
            NodeId = "node-approval",
            Prompt = "Approve the budget?",
            Requester = TestData.Assistant,
            Assignees = new List<Participant> { TestData.Teacher },
        }, Ct);

        await Assert.ThrowsAsync<ProtocolValidationException>(() => service.AnswerTaskAsync(task.Id, new AnswerTaskRequest
        {
            RespondedBy = TestData.Teacher,
            Text = "looks fine but no decision",
        }, Ct));
    }

    [Fact]
    public async Task AnswerTask_Twice_ThrowsConflict()
    {
        var service = CreateService();

        var task = await service.CreateTaskAsync(new CreateTaskRequest
        {
            Kind = HumanTaskKind.Input,
            WorkflowRef = "workflow:once",
            NodeId = "node-once",
            Prompt = "Only answer once.",
            Requester = TestData.Assistant,
            Assignees = new List<Participant> { TestData.Teacher },
        }, Ct);

        await service.AnswerTaskAsync(task.Id, new AnswerTaskRequest { RespondedBy = TestData.Teacher, Text = "first" }, Ct);

        var ex = await Assert.ThrowsAsync<LocalApiException>(() =>
            service.AnswerTaskAsync(task.Id, new AnswerTaskRequest { RespondedBy = TestData.Teacher, Text = "second" }, Ct));

        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task ListTasks_FiltersByStatusToken()
    {
        var service = CreateService();

        var created = await service.CreateTaskAsync(new CreateTaskRequest
        {
            Kind = HumanTaskKind.Input,
            WorkflowRef = "workflow:list",
            NodeId = "node-list",
            Prompt = "List me.",
            Requester = TestData.Assistant,
            Assignees = new List<Participant> { TestData.Teacher },
        }, Ct);

        var requested = await service.ListTasksAsync("REQUESTED", Ct);
        Assert.Contains(requested, t => t.Id == created.Id);

        var completed = await service.ListTasksAsync("COMPLETED", Ct);
        Assert.DoesNotContain(completed, t => t.Id == created.Id);
    }

    // -----------------------------------------------------------------------------------------------
    // Artifacts
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task RegisterArtifact_ThenGetReturnsMetadata()
    {
        var service = CreateService();

        var artifact = await service.RegisterArtifactAsync(new RegisterArtifactRequest
        {
            Hash = "sha256:0000000000000000000000000000000000000000000000000000000000000000",
            SizeBytes = 12,
            MimeType = "application/pdf",
            Filename = "evidence.pdf",
        }, Ct);

        Assert.False(string.IsNullOrEmpty(artifact.Id));
        Assert.Equal("evidence.pdf", artifact.Filename);

        var fetched = await service.GetArtifactAsync(artifact.Id, Ct);
        Assert.NotNull(fetched);
        Assert.Equal(artifact.Hash, fetched!.Hash);
    }

    [Fact]
    public async Task RegisterArtifact_DuplicateId_ThrowsConflict()
    {
        var service = CreateService();

        var id = "artifact:00000000-0000-0000-0000-000000000001";
        await service.RegisterArtifactAsync(new RegisterArtifactRequest
        {
            Id = id,
            Hash = "sha256:0000000000000000000000000000000000000000000000000000000000000000",
            SizeBytes = 1,
            MimeType = "text/plain",
            Filename = "a.txt",
        }, Ct);

        var ex = await Assert.ThrowsAsync<LocalApiException>(() => service.RegisterArtifactAsync(new RegisterArtifactRequest
        {
            Id = id,
            Hash = "sha256:1111111111111111111111111111111111111111111111111111111111111111",
            SizeBytes = 1,
            MimeType = "text/plain",
            Filename = "b.txt",
        }, Ct));

        Assert.Equal(409, ex.StatusCode);
    }

    // -----------------------------------------------------------------------------------------------
    // Sync status
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetSyncStatus_ReportsGatewayAndDeliveryCounts()
    {
        var service = CreateService();

        var conversation = await service.CreateConversationAsync(new CreateConversationRequest
        {
            Participants = new List<Participant> { TestData.Teacher, TestData.Assistant },
        }, Ct);

        await service.SendMessageAsync(new SendMessageRequest
        {
            Sender = TestData.Assistant,
            Recipients = new List<Participant> { TestData.Teacher },
            ConversationId = conversation.Id,
            Payload = new MessagePayload { Body = "status check" },
        }, Ct);

        var status = await service.GetSyncStatusAsync(Ct);
        Assert.Equal(GatewayId, status.GatewayId);
        Assert.Equal(1, status.Queued);
        Assert.True(status.LastSequence >= 1);
        Assert.Equal(1, status.Deliveries.Queued);
        Assert.Equal(0, status.Deliveries.Delivered);
        Assert.Equal(0, status.Deliveries.Failed);
    }
}
