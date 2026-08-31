using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Api;
using HumanGateway.Relay.Services;

namespace HumanGateway.Relay.Endpoints;

/// <summary>
/// Gateway registration endpoints (RELAY-FR-03, AUTH-FR-01, SP-02, SP-07). Handlers stay thin — all state
/// transitions and token verification live in <see cref="GatewayService"/>; exceptions translate to
/// <see cref="ProtocolError"/> responses by the global exception handler. The plaintext registration token is
/// returned exactly once, never persisted, and never logged (SP-07).
/// </summary>
public static class GatewayEndpoints
{
    /// <summary>Maps the gateway registration endpoint group onto the app.</summary>
    public static void MapGatewayEndpoints(this WebApplication app)
    {
        app.MapPost("/gateways", RequestRegistrationAsync);
        app.MapPost("/gateways/{gatewayId}/register", ConfirmRegistrationAsync);
        app.MapPost("/gateways/{gatewayId}/rotate", RotateTokenAsync);
    }

    /// <summary>Step 1 — requests a registration token; the identity is created in PENDING (201).</summary>
    private static async Task<IResult> RequestRegistrationAsync(
        GatewayService service, RegisterGatewayRequest request, CancellationToken ct)
    {
        var issued = await service.RequestRegistrationAsync(request, ct);
        return Results.Json(issued, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>Step 2 — presents the token and completes registration (200 with the full Gateway record).</summary>
    private static async Task<IResult> ConfirmRegistrationAsync(
        string gatewayId, ConfirmRegistrationRequest request, GatewayService service, CancellationToken ct)
    {
        var mismatch = EnsureRouteMatches(gatewayId, request.GatewayId);
        if (mismatch is not null)
        {
            return mismatch;
        }

        var record = await service.ConfirmRegistrationAsync(request, ct);
        return Results.Ok(record.ToProtocol());
    }

    /// <summary>Rotates a registered gateway's token (200 with a fresh one-time token).</summary>
    private static async Task<IResult> RotateTokenAsync(
        string gatewayId, ConfirmRegistrationRequest request, GatewayService service, CancellationToken ct)
    {
        var mismatch = EnsureRouteMatches(gatewayId, request.GatewayId);
        if (mismatch is not null)
        {
            return mismatch;
        }

        var issued = await service.RotateTokenAsync(request, ct);
        return Results.Ok(issued);
    }

    /// <summary>Rejects a request whose route gatewayId differs from the body gatewayId (defence-in-depth).</summary>
    private static IResult? EnsureRouteMatches(string routeGatewayId, string bodyGatewayId)
        => string.Equals(routeGatewayId, bodyGatewayId, StringComparison.Ordinal)
            ? null
            : ApiErrors.BadRequest(ErrorCodes.BadRequest,
                "The gatewayId in the URL path does not match the gatewayId in the request body.");
}
