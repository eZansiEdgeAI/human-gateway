using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Api;
using HumanGateway.Relay.Services;

namespace HumanGateway.Relay.Endpoints;

/// <summary>
/// Rendezvous endpoints (RELAY-FR-03, WEBX-FR-02): remote clients discover which registered school Edge
/// serves a participant and whether it is reachable. Routing is outbound-only (SP-01) — the Relay never dials
/// into the school; message delivery rides the gateway's next sync pull.
/// </summary>
public static class RendezvousEndpoints
{
    /// <summary>Maps the rendezvous endpoint group onto the app.</summary>
    public static void MapRendezvousEndpoints(this WebApplication app)
    {
        app.MapGet("/rendezvous/gateways", ListGatewaysAsync);
        app.MapGet("/rendezvous/gateways/{gatewayId}", GetGatewayAsync);
        app.MapGet("/rendezvous/lookup", LookupParticipantAsync);
    }

    /// <summary>Lists every registered gateway as a rendezvous target.</summary>
    private static async Task<IResult> ListGatewaysAsync(RendezvousService service, CancellationToken ct)
        => Results.Ok(await service.ListGatewaysAsync(ct));

    /// <summary>Rendezvous info for one registered gateway (404 when absent or not registered — SP-02).</summary>
    private static async Task<IResult> GetGatewayAsync(string gatewayId, RendezvousService service, CancellationToken ct)
    {
        var info = await service.GetGatewayAsync(gatewayId, ct);
        return info is null
            ? ApiErrors.NotFound($"Gateway '{gatewayId}' is not a registered rendezvous target (SP-02).")
            : Results.Ok(info);
    }

    /// <summary>Resolves a participant address to its serving gateway (400 on a malformed address).</summary>
    private static async Task<IResult> LookupParticipantAsync(string? participant, RendezvousService service, CancellationToken ct)
    {
        if (participant is null)
        {
            return ApiErrors.BadRequest(ErrorCodes.BadRequest,
                "participant query parameter is required — (human|agent|system):suffix (PROTO-FR-02).");
        }

        // A system: participant's suffix IS the gatewayId (gateway.schema.json, PROTO-FR-02), and durable IDs
        // may themselves contain ':' (e.g. "gateway:school-01"), which the participant-address charset does
        // not. Validate the suffix as a gatewayId for system: addresses and as a participant address otherwise.
        var wellFormed = participant.StartsWith("system:", StringComparison.Ordinal)
            ? RequestValidation.GatewayIdRegex().IsMatch(participant["system:".Length..])
            : RequestValidation.ParticipantAddressRegex().IsMatch(participant);

        if (!wellFormed)
        {
            return ApiErrors.BadRequest(ErrorCodes.BadRequest,
                "participant must be a typed address — (human|agent|system):suffix (PROTO-FR-02).");
        }

        var lookup = await service.LookupParticipantAsync(participant, ct);
        return lookup is null
            ? ApiErrors.NotFound($"No registered gateway serves participant '{participant}' (SP-02).")
            : Results.Ok(lookup);
    }
}
