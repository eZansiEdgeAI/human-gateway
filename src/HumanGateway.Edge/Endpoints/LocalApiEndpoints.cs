using HumanGateway.Edge.Api;

namespace HumanGateway.Edge.Endpoints;

/// <summary>
/// Maps the Edge local REST API (EDGE-FR-03, product vision §6.3): conversations, messages, tasks, artifacts,
/// and sync status. Handlers stay thin — the domain logic lives in <see cref="LocalApiService"/>, and
/// exceptions raised there are translated to <see cref="HumanGateway.Protocol.Models.ProtocolError"/> responses
/// by the global exception handler wired in <c>Program.cs</c>.
/// </summary>
public static class LocalApiEndpoints
{
    /// <summary>Maps every local API endpoint onto the app.</summary>
    public static void MapLocalApiEndpoints(this WebApplication app)
    {
        app.MapConversationEndpoints();
        app.MapMessageEndpoints();
        app.MapTaskEndpoints();
        app.MapArtifactEndpoints();
        app.MapSyncStatusEndpoints();
    }

    private static void MapConversationEndpoints(this WebApplication app)
    {
        app.MapGet("/conversations", static async (LocalApiService service, CancellationToken ct) =>
            Results.Ok(await service.ListConversationsAsync(ct)));

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
        app.MapPost("/messages", static async (SendMessageRequest request, LocalApiService service, CancellationToken ct) =>
        {
            var view = await service.SendMessageAsync(request, ct);
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
        app.MapPost("/tasks", static async (CreateTaskRequest request, LocalApiService service, CancellationToken ct) =>
        {
            var task = await service.CreateTaskAsync(request, ct);
            return Results.Created($"/tasks/{task.Id}", task);
        });

        app.MapGet("/tasks", static async (string? status, LocalApiService service, CancellationToken ct) =>
            Results.Ok(await service.ListTasksAsync(status, ct)));

        app.MapGet("/tasks/{id}", static async (string id, LocalApiService service, CancellationToken ct) =>
        {
            var task = await service.GetTaskAsync(id, ct);
            return task is null ? ApiErrors.NotFound($"Task {id} not found.") : Results.Ok(task);
        });

        app.MapPost("/tasks/{id}/response", static async (string id, AnswerTaskRequest request, LocalApiService service, CancellationToken ct) =>
        {
            var task = await service.AnswerTaskAsync(id, request, ct);
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

        app.MapGet("/artifacts", static async (LocalApiService service, CancellationToken ct) =>
            Results.Ok(await service.ListArtifactsAsync(ct)));

        app.MapGet("/artifacts/{id}", static async (string id, LocalApiService service, CancellationToken ct) =>
        {
            var artifact = await service.GetArtifactAsync(id, ct);
            return artifact is null ? ApiErrors.NotFound($"Artifact {id} not found.") : Results.Ok(artifact);
        });
    }

    private static void MapSyncStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/sync/status", static async (LocalApiService service, CancellationToken ct) =>
            Results.Ok(await service.GetSyncStatusAsync(ct)));
    }
}
