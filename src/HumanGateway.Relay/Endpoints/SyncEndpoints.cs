using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Api;
using HumanGateway.Relay.Services;

namespace HumanGateway.Relay.Endpoints;

/// <summary>
/// The Relay's sync endpoints (RELAY-FR-02, SYNC-FR-01..07): <c>POST /sync/push</c> applies a gateway's PUSH
/// batch and returns its new push cursor; <c>POST /sync/pull</c> returns the gateway's inbound PULL batch
/// after its echoed cursor. Handlers stay thin — all cursor/idempotency/routing logic lives in
/// <see cref="RelaySyncService"/>, and exceptions translate to <see cref="ProtocolError"/> responses by the
/// global exception handler (SP-07). The Edge always dials out to these endpoints; the Relay never dials in
/// (SP-01).
/// </summary>
public static class SyncEndpoints
{
    /// <summary>Maps the sync endpoint group onto the app.</summary>
    public static void MapSyncEndpoints(this WebApplication app)
    {
        app.MapPost("/sync/push", PushAsync);
        app.MapPost("/sync/pull", PullAsync);
    }

    /// <summary>
    /// Applies a gateway's PUSH batch (syncbatch.schema.json) and responds with the result batch carrying the
    /// new push cursor. The request body is the canonical wire SyncBatch; the response is a keepalive result
    /// batch (empty items) whose <c>cursor</c> is the durable acknowledgement the Edge flushes its outbox on.
    /// </summary>
    private static async Task<IResult> PushAsync(RelaySyncService service, SyncBatch batch, CancellationToken ct)
    {
        var response = await service.PushAsync(batch, ct);
        return Results.Ok(response);
    }

    /// <summary>
    /// Returns the gateway's inbound PULL batch (items addressed to it plus the new pull cursor). An empty
    /// items array is a valid keepalive — there is nothing new after the echoed cursor (SYNC-FR-03).
    /// </summary>
    private static async Task<IResult> PullAsync(SyncPullRequest request, RelaySyncService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var response = await service.PullAsync(request.GatewayId, request.SinceCursor, ct);
        return Results.Ok(response);
    }
}
