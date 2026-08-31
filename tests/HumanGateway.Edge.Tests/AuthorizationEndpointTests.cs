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
/// Security negative tests for the authorisation middleware (IDENTITY-SECURITY-5.3, AUTH-FR-03, SP-04):
/// cross-conversation/task/artifact access is denied with the reserved <c>CONVERSATION_ACCESS_DENIED</c> /
/// <c>TASK_ACCESS_DENIED</c> / <c>ARTIFACT_ACCESS_DENIED</c> codes, unauthenticated requests to protected
/// routes are rejected with 401, list endpoints filter to the caller's membership, and a user cannot act as
/// another participant. This is the test suite for identity-security §6: "Cross-conversation access denied".
/// </summary>
public sealed class AuthorizationEndpointTests : IClassFixture<AuthorizationEndpointTests.AuthFactory>
{
    private readonly AuthFactory _factory;

    public AuthorizationEndpointTests(AuthFactory factory) => _factory = factory;

    public sealed class AuthFactory : WebApplicationFactory<Program>
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "hgedge-authz-" + Guid.NewGuid().ToString("N"));

        public AuthFactory() => Directory.CreateDirectory(_dir);

        /// <summary>The seeded bootstrap credentials — the "teacher" account.</summary>
        public (string Username, string Password) Teacher { get; } = ("teacher", "teacher-pw");

        /// <summary>The provisioned "student" account (created on first use by <see cref="CreateStudentClientAsync"/>).</summary>
        public (string Username, string Password) Student { get; } = ("student-reviewer", "student-pw");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_dir, "edge.db"),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString();

            builder.UseSetting("ConnectionStrings:Edge", connectionString);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Artifacts:RootPath"] = Path.Combine(_dir, "artifacts"),
                    ["Auth:BootstrapUser:Username"] = Teacher.Username,
                    ["Auth:BootstrapUser:Password"] = Teacher.Password,
                    ["Auth:BootstrapUser:DisplayName"] = "Teacher",
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
                // Best-effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup.
            }
        }
    }

    // -----------------------------------------------------------------------------------------------
    // Session gate
    // -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("/conversations")]
    [InlineData("/conversations/any")]
    [InlineData("/messages/any")]
    [InlineData("/tasks")]
    [InlineData("/tasks/any")]
    [InlineData("/artifacts")]
    [InlineData("/artifacts/any")]
    [InlineData("/sync/status")]
    public async Task ProtectedRoutes_WithoutSession_Return401(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("UNAUTHORIZED", doc.RootElement.GetProperty("code").GetString());
        Assert.False(doc.RootElement.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task HealthProbe_RemainsPublic_NoSessionNeeded()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // -----------------------------------------------------------------------------------------------
    // Cross-conversation access denied
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Conversation_OtherUserNotMember_IsDenied()
    {
        var teacher = await CreateTeacherClientAsync();
        var student = await CreateStudentClientAsync();

        var conversationId = await CreateTeacherConversationAsync(teacher);

        var list = await student.GetAsync("/conversations");
        using (var listDoc = JsonDocument.Parse(await list.Content.ReadAsStringAsync()))
        {
            Assert.DoesNotContain(listDoc.RootElement.EnumerateArray(), c => c.GetProperty("id").GetString() == conversationId);
        }

        var get = await student.GetAsync($"/conversations/{conversationId}");
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal("CONVERSATION_ACCESS_DENIED", await ErrorCodeAsync(get));

        var messages = await student.GetAsync($"/conversations/{conversationId}/messages");
        Assert.Equal(HttpStatusCode.Forbidden, messages.StatusCode);
        Assert.Equal("CONVERSATION_ACCESS_DENIED", await ErrorCodeAsync(messages));
    }

    [Fact]
    public async Task Message_OtherUserNotMember_IsDenied()
    {
        var teacher = await CreateTeacherClientAsync();
        var student = await CreateStudentClientAsync();

        var (conversationId, messageId) = await CreateTeacherConversationAndMessageAsync(teacher);

        var response = await student.GetAsync($"/messages/{messageId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("CONVERSATION_ACCESS_DENIED", await ErrorCodeAsync(response));

        // The teacher, who is a member, reads it fine.
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync($"/messages/{messageId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync($"/conversations/{conversationId}/messages")).StatusCode);
    }

    // -----------------------------------------------------------------------------------------------
    // Cross-task access denied
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Task_OtherUserNotMember_IsDenied()
    {
        var teacher = await CreateTeacherClientAsync();
        var student = await CreateStudentClientAsync();

        var taskId = await CreateTeacherTaskAsync(teacher);

        var response = await student.GetAsync($"/tasks/{taskId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("TASK_ACCESS_DENIED", await ErrorCodeAsync(response));

        // The teacher, who is assigned, reads it fine.
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync($"/tasks/{taskId}")).StatusCode);
    }

    [Fact]
    public async Task AnswerTask_ByConversationMemberWhoIsNotAssigned_IsDenied()
    {
        var teacher = await CreateTeacherClientAsync();
        var student = await CreateStudentClientAsync();

        // A conversation with both teacher and student; the task is assigned to the teacher only.
        var teacherMe = await MeAsync(teacher);
        var studentMe = await MeAsync(student);
        var conversation = await teacher.PostAsync("/conversations", JsonBody(new
        {
            title = "Shared",
            participants = new object[]
            {
                TeacherParticipant(teacherMe),
                StudentParticipant(studentMe),
                AgentParticipant(),
            },
        }));
        var conversationId = IdOf(conversation);

        var createTask = await teacher.PostAsync("/tasks", JsonBody(new
        {
            kind = "input",
            workflowRef = "workflow:shared-conversation",
            nodeId = "node-shared",
            prompt = "Only the teacher answers.",
            requester = AgentParticipant(),
            assignees = new[] { TeacherParticipant(teacherMe) },
            conversationId,
        }));
        Assert.Equal(HttpStatusCode.Created, createTask.StatusCode);
        var taskId = IdOf(createTask);

        // The student is a member of the task's conversation (middleware passes) but not an assigned
        // recipient — the service layer denies the answer (AUTH-US-01: only your own tasks).
        var answer = await student.PostAsync($"/tasks/{taskId}/response", JsonBody(new
        {
            respondedBy = StudentParticipant(studentMe),
            text = "I was not asked",
        }));

        Assert.Equal(HttpStatusCode.Forbidden, answer.StatusCode);
        Assert.Equal("TASK_ACCESS_DENIED", await ErrorCodeAsync(answer));
    }

    // -----------------------------------------------------------------------------------------------
    // Cross-artifact access denied (AUTH-FR-05)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task ArtifactDownload_NotReferencedInAccessibleConversation_IsDenied()
    {
        var teacher = await CreateTeacherClientAsync();
        var student = await CreateStudentClientAsync();

        var (_, artifactId) = await CreateTeacherConversationMessageAndArtifactAsync(teacher);

        // Artifact metadata is session-only (the creator's register→upload→attach flow needs it), but the
        // download is conversation-gated (AUTH-FR-05): the student, not a member of any referencing
        // conversation, is denied the bytes.
        var download = await student.GetAsync($"/artifacts/{artifactId}/content");
        Assert.Equal(HttpStatusCode.Forbidden, download.StatusCode);
        Assert.Equal("ARTIFACT_ACCESS_DENIED", await ErrorCodeAsync(download));

        // The teacher, whose conversation references it, passes the authorisation gate (bytes are not
        // uploaded here, so the service returns 404 — authorised but absent).
        var teacherDownload = await teacher.GetAsync($"/artifacts/{artifactId}/content");
        Assert.Equal(HttpStatusCode.NotFound, teacherDownload.StatusCode);
    }

    [Fact]
    public async Task ArtifactList_OtherUsersArtifacts_AreNotVisible()
    {
        var teacher = await CreateTeacherClientAsync();
        var student = await CreateStudentClientAsync();

        var (_, artifactId) = await CreateTeacherConversationMessageAndArtifactAsync(teacher);

        var list = await student.GetAsync("/artifacts");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.DoesNotContain(doc.RootElement.EnumerateArray(), a => a.GetProperty("id").GetString() == artifactId);
    }

    // -----------------------------------------------------------------------------------------------
    // No cross-participant writes
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task SendMessage_AsAnotherParticipant_IsDenied()
    {
        var teacher = await CreateTeacherClientAsync();
        var student = await CreateStudentClientAsync();
        var teacherMe = await MeAsync(teacher);

        var conversationId = await CreateTeacherConversationAsync(teacher);

        // The student (authenticated) tries to compose a message whose sender is the teacher.
        var send = await student.PostAsync("/messages", JsonBody(new
        {
            sender = TeacherParticipant(teacherMe),
            recipients = new[] { AgentParticipant() },
            conversationId,
            payload = new { body = "impersonating the teacher" },
        }));

        Assert.Equal(HttpStatusCode.Forbidden, send.StatusCode);
        Assert.Equal("FORBIDDEN", await ErrorCodeAsync(send));
    }

    [Fact]
    public async Task SendMessage_ToConversationNotAMemberOf_IsDenied()
    {
        var teacher = await CreateTeacherClientAsync();
        var student = await CreateStudentClientAsync();
        var studentMe = await MeAsync(student);

        var conversationId = await CreateTeacherConversationAsync(teacher);

        // The student is not a member of the teacher's conversation, so they cannot write into it.
        var send = await student.PostAsync("/messages", JsonBody(new
        {
            sender = StudentParticipant(studentMe),
            recipients = new[] { AgentParticipant() },
            conversationId,
            payload = new { body = "intrusion" },
        }));

        Assert.Equal(HttpStatusCode.Forbidden, send.StatusCode);
        Assert.Equal("CONVERSATION_ACCESS_DENIED", await ErrorCodeAsync(send));
    }

    [Fact]
    public async Task CreateTask_InConversationNotAMemberOf_IsDenied()
    {
        var teacher = await CreateTeacherClientAsync();
        var student = await CreateStudentClientAsync();
        var studentMe = await MeAsync(student);

        var conversationId = await CreateTeacherConversationAsync(teacher);

        var createTask = await student.PostAsync("/tasks", JsonBody(new
        {
            kind = "input",
            workflowRef = "workflow:intrusion",
            nodeId = "node-intrusion",
            prompt = "Inject into the teacher's conversation.",
            requester = AgentParticipant(),
            assignees = new[] { StudentParticipant(studentMe) },
            conversationId,
        }));

        Assert.Equal(HttpStatusCode.Forbidden, createTask.StatusCode);
        Assert.Equal("CONVERSATION_ACCESS_DENIED", await ErrorCodeAsync(createTask));
    }

    [Fact]
    public async Task CreateTask_AsAnotherHumanRequester_IsDenied()
    {
        var teacher = await CreateTeacherClientAsync();
        var student = await CreateStudentClientAsync();
        var teacherMe = await MeAsync(teacher);
        var studentMe = await MeAsync(student);

        // The student tries to create a task whose (human) requester is the teacher.
        var createTask = await student.PostAsync("/tasks", JsonBody(new
        {
            kind = "input",
            workflowRef = "workflow:impersonate-requester",
            nodeId = "node-impersonate",
            prompt = "Asked by the teacher?",
            requester = TeacherParticipant(teacherMe),
            assignees = new[] { StudentParticipant(studentMe) },
        }));

        Assert.Equal(HttpStatusCode.Forbidden, createTask.StatusCode);
        Assert.Equal("FORBIDDEN", await ErrorCodeAsync(createTask));
    }

    // -----------------------------------------------------------------------------------------------
    // List filtering (no cross-participant access)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Lists_AreFilteredToTheCallersMembership()
    {
        var teacher = await CreateTeacherClientAsync();
        var student = await CreateStudentClientAsync();

        var conversationId = await CreateTeacherConversationAsync(teacher);

        // The student has no linked participant yet: every list is empty, not forbidden (no leak).
        var conversations = await student.GetAsync("/conversations");
        Assert.Equal(HttpStatusCode.OK, conversations.StatusCode);
        using (var doc = JsonDocument.Parse(await conversations.Content.ReadAsStringAsync()))
        {
            Assert.DoesNotContain(doc.RootElement.EnumerateArray(), c => c.GetProperty("id").GetString() == conversationId);
        }

        var tasks = await student.GetAsync("/tasks");
        Assert.Equal(HttpStatusCode.OK, tasks.StatusCode);
        using (var doc = JsonDocument.Parse(await tasks.Content.ReadAsStringAsync()))
        {
            Assert.Empty(doc.RootElement.EnumerateArray());
        }

        var artifacts = await student.GetAsync("/artifacts");
        Assert.Equal(HttpStatusCode.OK, artifacts.StatusCode);
        using (var doc = JsonDocument.Parse(await artifacts.Content.ReadAsStringAsync()))
        {
            Assert.Empty(doc.RootElement.EnumerateArray());
        }
    }

    // -----------------------------------------------------------------------------------------------
    // Positive control: a member accesses their own resources
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Member_CanAccessTheirOwnConversationMessageTaskArtifact()
    {
        var teacher = await CreateTeacherClientAsync();

        var (conversationId, messageId) = await CreateTeacherConversationAndMessageAsync(teacher);
        var taskId = await CreateTeacherTaskAsync(teacher);
        var (_, artifactId) = await CreateTeacherConversationMessageAndArtifactAsync(teacher);

        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync($"/conversations/{conversationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync($"/messages/{messageId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync($"/tasks/{taskId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync($"/artifacts/{artifactId}")).StatusCode);
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    private async Task<HttpClient> CreateTeacherClientAsync() => await CreateAuthenticatedClientAsync(_factory.Teacher.Username, _factory.Teacher.Password);

    /// <summary>Creates (on first use) or reuses the student account and returns an authenticated client.</summary>
    private async Task<HttpClient> CreateStudentClientAsync() => await CreateAuthenticatedClientAsync(_factory.Student.Username, _factory.Student.Password);

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string username, string password)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsync("/auth/login", JsonBody(new { username, password }));
        if (login.StatusCode == HttpStatusCode.Unauthorized)
        {
            var created = await client.PostAsync("/auth/users", JsonBody(new { username, displayName = username, password }));
            Assert.True(created.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
                $"expected provisioned user {username}, got {created.StatusCode}");
        }

        var token = await LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Resolves the authenticated user's id (used to link the caller's participant — AUTH-FR-02).</summary>
    private static async Task<string> MeAsync(HttpClient client)
    {
        var me = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var doc = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("userId").GetString()!;
    }

    /// <summary>Creates a teacher-only conversation and returns its id.</summary>
    private async Task<string> CreateTeacherConversationAsync(HttpClient teacher)
    {
        var teacherMe = await MeAsync(teacher);
        var created = await teacher.PostAsync("/conversations", JsonBody(new
        {
            title = "Teacher private",
            participants = new object[] { TeacherParticipant(teacherMe), AgentParticipant() },
        }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return IdOf(created);
    }

    private async Task<(string ConversationId, string MessageId)> CreateTeacherConversationAndMessageAsync(HttpClient teacher)
    {
        var conversationId = await CreateTeacherConversationAsync(teacher);
        var teacherMe = await MeAsync(teacher);
        var sent = await teacher.PostAsync("/messages", JsonBody(new
        {
            sender = TeacherParticipant(teacherMe),
            recipients = new[] { AgentParticipant() },
            conversationId,
            payload = new { body = "hello" },
        }));
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        using var doc = JsonDocument.Parse(await sent.Content.ReadAsStringAsync());
        return (conversationId, doc.RootElement.GetProperty("message").GetProperty("id").GetString()!);
    }

    private async Task<string> CreateTeacherTaskAsync(HttpClient teacher)
    {
        var teacherMe = await MeAsync(teacher);
        var created = await teacher.PostAsync("/tasks", JsonBody(new
        {
            kind = "input",
            workflowRef = "workflow:teacher-task",
            nodeId = "node-teacher-task",
            prompt = "Answer the teacher's task.",
            requester = AgentParticipant(),
            assignees = new[] { TeacherParticipant(teacherMe) },
        }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return IdOf(created);
    }

    private async Task<(string ConversationId, string ArtifactId)> CreateTeacherConversationMessageAndArtifactAsync(HttpClient teacher)
    {
        var (conversationId, _) = await CreateTeacherConversationAndMessageAsync(teacher);
        var teacherMe = await MeAsync(teacher);

        var register = await teacher.PostAsync("/artifacts", JsonBody(new
        {
            hash = "sha256:0000000000000000000000000000000000000000000000000000000000000000",
            sizeBytes = 12,
            mimeType = "application/pdf",
            filename = "evidence.pdf",
        }));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var artifactId = IdOf(register);

        // Reference the artifact from the teacher's conversation so only its members can access it (AUTH-FR-05).
        var attach = await teacher.PostAsync("/messages", JsonBody(new
        {
            sender = TeacherParticipant(teacherMe),
            recipients = new[] { AgentParticipant() },
            conversationId,
            payload = new { body = "evidence attached" },
            artifactRefs = new[]
            {
                new { id = artifactId, hash = "sha256:0000000000000000000000000000000000000000000000000000000000000000", filename = "evidence.pdf", mimeType = "application/pdf", sizeBytes = 12 },
            },
        }));
        Assert.Equal(HttpStatusCode.Created, attach.StatusCode);

        return (conversationId, artifactId);
    }

    private static object TeacherParticipant(string userId) => new
    {
        address = "human:teacher@school.example",
        kind = "human",
        displayName = "Teacher",
        userId,
    };

    private static object StudentParticipant(string? userId) => new
    {
        address = "human:student@school.example",
        kind = "human",
        displayName = "Student",
        userId,
    };

    private static object AgentParticipant() => new
    {
        address = "agent:assistant@school.example",
        kind = "agent",
        displayName = "Assistant",
        userId = (string?)null,
    };

    private static string IdOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
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
