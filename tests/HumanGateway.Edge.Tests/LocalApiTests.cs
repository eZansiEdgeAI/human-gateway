using HumanGateway.Edge.Api;
using HumanGateway.Edge.Artifacts;
using HumanGateway.Edge.Storage;
using HumanGateway.Protocol.Models;
using HumanGateway.Protocol.Validation;
using HumanGateway.Security;
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

    /// <summary>The teacher's authenticated identity (matches TestData.Teacher's UserId).</summary>
    private static readonly AuthenticatedUser TeacherUser = new()
    {
        UserId = TestData.Teacher.UserId!,
        Username = "teacher",
        DisplayName = "Teacher",
        ExpiresAt = "2099-01-01T00:00:00.000Z",
    };

    /// <summary>The student's authenticated identity (matches TestData.Student's UserId).</summary>
    private static readonly AuthenticatedUser StudentUser = new()
    {
        UserId = TestData.Student.UserId!,
        Username = "student",
        DisplayName = "Student",
        ExpiresAt = "2099-01-01T00:00:00.000Z",
    };

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

        var listed = await service.ListConversationsAsync(TeacherUser, Ct);
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
            Sender = TestData.Teacher,
            Recipients = new List<Participant> { TestData.Assistant },
            ConversationId = conversation.Id,
            Payload = new MessagePayload { Body = "hello", Format = MessageFormat.Plaintext },
        }, TeacherUser, Ct);

        // Message envelope round-trips with a computed content hash.
        Assert.False(string.IsNullOrEmpty(sent.Message.Id));
        Assert.Equal("hello", sent.Message.Payload.Body);
        Assert.StartsWith("sha256:", sent.Message.ContentHash);

        // One delivery per recipient, starting at QUEUED (the PWA status indicator's initial state).
        var delivery = Assert.Single(sent.Deliveries);
        Assert.Equal(DeliveryState.Queued, delivery.State);
        Assert.Equal(TestData.Assistant.Address, delivery.Recipient.Address);
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
            Sender = TestData.Teacher,
            Recipients = new List<Participant>(),
            ConversationId = conversation.Id,
            Payload = new MessagePayload { Body = "nobody home" },
        }, TeacherUser, Ct));
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
        }, TeacherUser, Ct);

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
        }, TeacherUser, Ct);

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
        }, TeacherUser, Ct);

        await Assert.ThrowsAsync<ProtocolValidationException>(() => service.AnswerTaskAsync(task.Id, new AnswerTaskRequest
        {
            RespondedBy = TestData.Teacher,
            Text = "looks fine but no decision",
        }, TeacherUser, Ct));
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
        }, TeacherUser, Ct);

        await service.AnswerTaskAsync(task.Id, new AnswerTaskRequest { RespondedBy = TestData.Teacher, Text = "first" }, TeacherUser, Ct);

        var ex = await Assert.ThrowsAsync<LocalApiException>(() =>
            service.AnswerTaskAsync(task.Id, new AnswerTaskRequest { RespondedBy = TestData.Teacher, Text = "second" }, TeacherUser, Ct));

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
        }, TeacherUser, Ct);

        var requested = await service.ListTasksAsync("REQUESTED", TeacherUser, Ct);
        Assert.Contains(requested, t => t.Id == created.Id);

        var completed = await service.ListTasksAsync("COMPLETED", TeacherUser, Ct);
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
            Sender = TestData.Teacher,
            Recipients = new List<Participant> { TestData.Assistant },
            ConversationId = conversation.Id,
            Payload = new MessagePayload { Body = "status check" },
        }, TeacherUser, Ct);

        var status = await service.GetSyncStatusAsync(Ct);
        Assert.Equal(GatewayId, status.GatewayId);
        Assert.Equal(1, status.Queued);
        Assert.True(status.LastSequence >= 1);
        Assert.Equal(1, status.Deliveries.Queued);
        Assert.Equal(0, status.Deliveries.Delivered);
        Assert.Equal(0, status.Deliveries.Failed);
    }

    // -----------------------------------------------------------------------------------------------
    // Authorisation (AUTH-FR-03, SP-04): no cross-participant access
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task SendMessage_AsAnotherParticipant_ThrowsForbidden()
    {
        var service = CreateService();
        var conversation = await service.CreateConversationAsync(new CreateConversationRequest
        {
            Participants = new List<Participant> { TestData.Teacher, TestData.Assistant },
        }, Ct);

        // The teacher user tries to send a message whose sender is the student — cross-participant write.
        var ex = await Assert.ThrowsAsync<LocalApiException>(() => service.SendMessageAsync(new SendMessageRequest
        {
            Sender = TestData.Student,
            Recipients = new List<Participant> { TestData.Assistant },
            ConversationId = conversation.Id,
            Payload = new MessagePayload { Body = "impersonation" },
        }, TeacherUser, Ct));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal(ErrorCodes.Forbidden, ex.Code);
    }

    [Fact]
    public async Task SendMessage_ToConversationNotAMemberOf_ThrowsForbidden()
    {
        var service = CreateService();
        var conversation = await service.CreateConversationAsync(new CreateConversationRequest
        {
            Participants = new List<Participant> { TestData.Teacher, TestData.Assistant },
        }, Ct);

        // The student is not a member of the teacher's conversation, so they cannot write into it.
        var ex = await Assert.ThrowsAsync<LocalApiException>(() => service.SendMessageAsync(new SendMessageRequest
        {
            Sender = TestData.Student,
            Recipients = new List<Participant> { TestData.Assistant },
            ConversationId = conversation.Id,
            Payload = new MessagePayload { Body = "intrusion" },
        }, StudentUser, Ct));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal(ErrorCodes.ConversationAccessDenied, ex.Code);
    }

    [Fact]
    public async Task SendMessage_ToNonMemberRecipient_ThrowsForbidden()
    {
        var service = CreateService();
        var conversation = await service.CreateConversationAsync(new CreateConversationRequest
        {
            Participants = new List<Participant> { TestData.Teacher, TestData.Assistant },
        }, Ct);

        // The teacher is a member, but the student recipient is not — the message must not be delivered
        // into a participant's inbox for a conversation they cannot access.
        var ex = await Assert.ThrowsAsync<LocalApiException>(() => service.SendMessageAsync(new SendMessageRequest
        {
            Sender = TestData.Teacher,
            Recipients = new List<Participant> { TestData.Student },
            ConversationId = conversation.Id,
            Payload = new MessagePayload { Body = "hello student" },
        }, TeacherUser, Ct));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal(ErrorCodes.ConversationAccessDenied, ex.Code);
    }

    [Fact]
    public async Task AnswerTask_ByNonAssigneeMember_ThrowsForbidden()
    {
        var service = CreateService();

        // Conversation with teacher + student; the task is assigned to the student only.
        var task = await service.CreateTaskAsync(new CreateTaskRequest
        {
            Kind = HumanTaskKind.Input,
            WorkflowRef = "workflow:assigned",
            NodeId = "node-assigned",
            Prompt = "Answer only if assigned.",
            Requester = TestData.Assistant,
            Assignees = new List<Participant> { TestData.Student },
        }, TeacherUser, Ct);

        // The teacher is a member of the task's conversation but was not assigned — answering is denied.
        var ex = await Assert.ThrowsAsync<LocalApiException>(() => service.AnswerTaskAsync(task.Id, new AnswerTaskRequest
        {
            RespondedBy = TestData.Teacher,
            Text = "not my task",
        }, TeacherUser, Ct));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal(ErrorCodes.TaskAccessDenied, ex.Code);
    }

    [Fact]
    public async Task AnswerTask_AsAnotherParticipant_ThrowsForbidden()
    {
        var service = CreateService();

        var task = await service.CreateTaskAsync(new CreateTaskRequest
        {
            Kind = HumanTaskKind.Input,
            WorkflowRef = "workflow:impersonate",
            NodeId = "node-impersonate",
            Prompt = "Answer me.",
            Requester = TestData.Assistant,
            Assignees = new List<Participant> { TestData.Teacher },
        }, TeacherUser, Ct);

        // The student user tries to answer as the teacher — cross-participant write.
        var ex = await Assert.ThrowsAsync<LocalApiException>(() => service.AnswerTaskAsync(task.Id, new AnswerTaskRequest
        {
            RespondedBy = TestData.Teacher,
            Text = "stolen answer",
        }, StudentUser, Ct));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal(ErrorCodes.Forbidden, ex.Code);
    }

    [Fact]
    public async Task ListConversations_FiltersToUserMembership()
    {
        var service = CreateService();

        var teacherConversation = await service.CreateConversationAsync(new CreateConversationRequest
        {
            Title = "Teacher only",
            Participants = new List<Participant> { TestData.Teacher, TestData.Assistant },
        }, Ct);
        await service.CreateConversationAsync(new CreateConversationRequest
        {
            Title = "Student only",
            Participants = new List<Participant> { TestData.Student, TestData.Assistant },
        }, Ct);

        var listed = await service.ListConversationsAsync(TeacherUser, Ct);

        Assert.Contains(listed, c => c.Id == teacherConversation.Id);
        Assert.DoesNotContain(listed, c => c.Title == "Student only");
    }

    [Fact]
    public async Task ListTasks_FiltersToMembershipOrAssignment()
    {
        var service = CreateService();

        var teacherTask = await service.CreateTaskAsync(new CreateTaskRequest
        {
            Kind = HumanTaskKind.Input,
            WorkflowRef = "workflow:teacher-task",
            NodeId = "node-1",
            Prompt = "For the teacher.",
            Requester = TestData.Assistant,
            Assignees = new List<Participant> { TestData.Teacher },
        }, TeacherUser, Ct);
        var studentTask = await service.CreateTaskAsync(new CreateTaskRequest
        {
            Kind = HumanTaskKind.Input,
            WorkflowRef = "workflow:student-task",
            NodeId = "node-2",
            Prompt = "For the student.",
            Requester = TestData.Assistant,
            Assignees = new List<Participant> { TestData.Student },
        }, TeacherUser, Ct);

        var listed = await service.ListTasksAsync(null, TeacherUser, Ct);

        Assert.Contains(listed, t => t.Id == teacherTask.Id);
        Assert.DoesNotContain(listed, t => t.Id == studentTask.Id);
    }

    [Fact]
    public async Task ListArtifacts_FiltersToReferencedInAccessibleConversations()
    {
        var service = CreateService();

        var teacherConversation = await service.CreateConversationAsync(new CreateConversationRequest
        {
            Participants = new List<Participant> { TestData.Teacher, TestData.Assistant },
        }, Ct);
        var studentConversation = await service.CreateConversationAsync(new CreateConversationRequest
        {
            Participants = new List<Participant> { TestData.Student, TestData.Assistant },
        }, Ct);

        var accessibleArtifact = await service.RegisterArtifactAsync(new RegisterArtifactRequest
        {
            Hash = "sha256:1111111111111111111111111111111111111111111111111111111111111111",
            SizeBytes = 5,
            MimeType = "text/plain",
            Filename = "visible.txt",
        }, Ct);
        var hiddenArtifact = await service.RegisterArtifactAsync(new RegisterArtifactRequest
        {
            Hash = "sha256:2222222222222222222222222222222222222222222222222222222222222222",
            SizeBytes = 5,
            MimeType = "text/plain",
            Filename = "hidden.txt",
        }, Ct);

        // Reference the accessible artifact from the teacher's conversation and the hidden one only from
        // the student's conversation.
        await service.SendMessageAsync(new SendMessageRequest
        {
            Sender = TestData.Teacher,
            Recipients = new List<Participant> { TestData.Assistant },
            ConversationId = teacherConversation.Id,
            Payload = new MessagePayload { Body = "with attachment" },
            ArtifactRefs = new List<ArtifactReference>
            {
                new() { Id = accessibleArtifact.Id, Hash = accessibleArtifact.Hash, Filename = "visible.txt", MimeType = "text/plain", SizeBytes = 5 },
            },
        }, TeacherUser, Ct);
        await service.SendMessageAsync(new SendMessageRequest
        {
            Sender = TestData.Student,
            Recipients = new List<Participant> { TestData.Assistant },
            ConversationId = studentConversation.Id,
            Payload = new MessagePayload { Body = "with attachment" },
            ArtifactRefs = new List<ArtifactReference>
            {
                new() { Id = hiddenArtifact.Id, Hash = hiddenArtifact.Hash, Filename = "hidden.txt", MimeType = "text/plain", SizeBytes = 5 },
            },
        }, StudentUser, Ct);

        var listed = await service.ListArtifactsAsync(TeacherUser, Ct);

        Assert.Contains(listed, a => a.Id == accessibleArtifact.Id);
        Assert.DoesNotContain(listed, a => a.Id == hiddenArtifact.Id);
    }
}
