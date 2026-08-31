using HumanGateway.Edge.Api;
using HumanGateway.Security;

namespace HumanGateway.Edge.Endpoints;

/// <summary>
/// Maps the Edge local REST API (EDGE-FR-03, product vision §6.3): conversations, messages, tasks, artifacts,
/// and sync status. Handlers stay thin — the domain logic lives in <see cref="LocalApiService"/>, and
/// exceptions raised there are translated to <see cref="HumanGateway.Protocol.Models.ProtocolError"/> responses
/// by the global exception handler wired in <c>Program.cs</c>.
///
/// Every route here is behind the authorisation middleware (AUTH-FR-03, SP-04): the middleware rejects
/// unauthenticated requests to these routes with 401 and enforces per-conversation/task/artifact access for
/// single-resource reads. Handlers therefore call <see cref="CurrentUser.Require"/> to resolve the
/// authenticated user for the service layer's list filters and write-actor checks (never null on a protected
/// route).
/// </summary>
public static class LocalApiEndpoints
{
    /// <summary>Maps every local API endpoint onto the app.</summary>
    public static void MapLocalApiEndpoints(this WebApplication app)
    {
        app.MapAuthEndpoints();
        app.MapConversationEndpoints();
        app.MapMessageEndpoints();
        app.MapTaskEndpoints();
        app.MapArtifactEndpoints();
        app.MapSyncStatusEndpoints();
    }

    private static void MapConversationEndpoints(this WebApplication app)
    {
        app.MapGet("/conversations", static async (HttpContext http, LocalApiService service, CancellationToken ct) =>
            Results.Ok(await service.ListConversationsAsync(CurrentUser.Require(http), ct)));

        app.MapPost("/conversations", static async (CreateConversationRequest request, LocalApiService service, CancellationToken ct) =>
        {
            var view = await service.CreateConversationAsync(request, ct);
            return Results.Created($"/conversations/{view.Id}", view);
        });

        app.MapGet("/conversations/{id}", static async (string id, LocalApiService service, CancellationToken ct) =>
        {
            var view = await service.GetConversationAsync(id, ct);
            return view is null ? ApiErrors.NotFound($"Conversation {id} not found.") : Results.Ok(view);
        });

        app.MapGet("/conversations/{id}/messages", static async (string id, LocalApiService service, CancellationToken ct) =>
            Results.Ok(await service.ListConversationMessagesAsync(id, ct)));
    }

    private static void MapMessageEndpoints(this WebApplication app)
    {
        app.MapPost("/messages", static async (SendMessageRequest request, HttpContext http, LocalApiService service, CancellationToken ct) =>
        {
            var view = await service.SendMessageAsync(request, CurrentUser.Require(http), ct);
            return Results.Created($"/messages/{view.Message.Id}", view);
        });

        app.MapGet("/messages/{id}", static async (string id, LocalApiService service, CancellationToken ct) =>
        {
            var view = await service.GetMessageAsync(id, ct);
            return view is null ? ApiErrors.NotFound($"Message {id} not found.") : Results.Ok(view);
        });
    }

    private static void MapTaskEndpoints(this WebApplication app)
    {
        app.MapPost("/tasks", static async (CreateTaskRequest request, HttpContext http, LocalApiService service, CancellationToken ct) =>
        {
            var task = await service.CreateTaskAsync(request, CurrentUser.Require(http), ct);
            return Results.Created($"/tasks/{task.Id}", task);
        });

        app.MapGet("/tasks", static async (string? status, HttpContext http, LocalApiService service, CancellationToken ct) =>
            Results.Ok(await service.ListTasksAsync(status, CurrentUser.Require(http), ct)));

        app.MapGet("/tasks/{id}", static async (string id, LocalApiService service, CancellationToken ct) =>
        {
            var task = await service.GetTaskAsync(id, ct);
            return task is null ? ApiErrors.NotFound($"Task {id} not found.") : Results.Ok(task);
        });

        app.MapPost("/tasks/{id}/response", static async (string id, AnswerTaskRequest request, HttpContext http, LocalApiService service, CancellationToken ct) =>
        {
            var task = await service.AnswerTaskAsync(id, request, CurrentUser.Require(http), ct);
            return task is null ? ApiErrors.NotFound($"Task {id} not found.") : Results.Ok(task);
        });
    }

    private static void MapArtifactEndpoints(this WebApplication app)
    {
        app.MapPost("/artifacts", static async (RegisterArtifactRequest request, LocalApiService service, CancellationToken ct) =>
        {
            var artifact = await service.RegisterArtifactAsync(request, ct);
            return Results.Created($"/artifacts/{artifact.Id}", artifact);
        });

        app.MapGet("/artifacts", static async (HttpContext http, LocalApiService service, CancellationToken ct) =>
            Results.Ok(await service.ListArtifactsAsync(CurrentUser.Require(http), ct)));

        app.MapGet("/artifacts/{id}", static async (string id, LocalApiService service, CancellationToken ct) =>
        {
            var artifact = await service.GetArtifactAsync(id, ct);
            return artifact is null ? ApiErrors.NotFound($"Artifact {id} not found.") : Results.Ok(artifact);
        });

        // Byte upload (ARTF-FR-01, ARTF-FR-03): raw body, hash-verified against the registered metadata,
        // size-limit + quota enforced. Deduplicated writes return 200 Stored=false (identical bytes already
        // on disk — no re-transfer, no quota).
        app.MapPut("/artifacts/{id}/content", static async (string id, HttpRequest request, LocalApiService service, CancellationToken ct) =>
        {
            var result = await service.UploadArtifactContentAsync(id, request.Body, request.ContentLength, ct);
            return result.Stored
                ? Results.Created($"/artifacts/{id}/content", result)
                : Results.Ok(result);
        });

        // Byte download: streamed with Range support (resumable downloads, ARTF-FR-02) and the artifact's
        // MIME type/filename so the receiving app can render or interpret the content (artifacts §5).
        app.MapGet("/artifacts/{id}/content", static async (string id, LocalApiService service, CancellationToken ct) =>
        {
            var (artifact, content) = await service.DownloadArtifactContentAsync(id, ct);
            if (content is null)
            {
                return ApiErrors.NotFound($"Artifact {id} metadata is registered but its bytes are not uploaded yet.");
            }

            return Results.File(content, artifact.MimeType, artifact.Filename, enableRangeProcessing: true);
        });

        // Presence + limits snapshot for dedup/resume queries and PWA size/quota messaging (ARTF-FR-03).
        app.MapGet("/artifacts/{id}/content/status", static async (string id, LocalApiService service, CancellationToken ct) =>
            Results.Ok(await service.GetArtifactContentStatusAsync(id, ct)));
    }

    private static void MapSyncStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/sync/status", static async (LocalApiService service, CancellationToken ct) =>
            Results.Ok(await service.GetSyncStatusAsync(ct)));
    }
}
