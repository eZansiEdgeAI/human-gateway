namespace HumanGateway.Relay.Endpoints;

/// <summary>
/// Maps the Cloud Relay HTTP API (RELAY-FR-02..04): the service-info probe, the gateway registration +
/// rendezvous endpoints (RELAY-FR-03, WEBX-FR-02), and the sync endpoint group (push/pull cursors + delivery
/// ack, SYNC-FR-03/05).
/// </summary>
public static class RelayEndpoints
{
    /// <summary>Maps every Relay endpoint onto the app.</summary>
    public static void MapRelayEndpoints(this WebApplication app)
    {
        app.MapRelayInfoEndpoint();
        app.MapGatewayEndpoints();
        app.MapRendezvousEndpoints();
        app.MapSyncEndpoints();
        app.MapRemoteMessageEndpoints();
    }

    private static void MapRelayInfoEndpoint(this WebApplication app)
    {
        // Service identity probe: instance name, assembly version, and DB-backed liveness. No secrets.
        app.MapGet("/relay", static () => Results.Ok(new
        {
            name = "humangateway-relay",
            version = typeof(Program).Assembly.GetName().Version?.ToString(),
        }));
    }
}
