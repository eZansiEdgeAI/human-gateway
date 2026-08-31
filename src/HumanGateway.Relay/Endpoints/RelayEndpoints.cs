namespace HumanGateway.Relay.Endpoints;

/// <summary>
/// Maps the Cloud Relay HTTP API (RELAY-FR-02..04). Carries the service-info probe, the gateway
/// registration + rendezvous endpoints (RELAY-FR-03, WEBX-FR-02), and — in a later task — the sync endpoint
/// (push/pull cursors + delivery ack, SYNC-FR-03/05, task CLOUD-RELAY-4.4) as its own group.
/// </summary>
public static class RelayEndpoints
{
    /// <summary>Maps every Relay endpoint onto the app.</summary>
    public static void MapRelayEndpoints(this WebApplication app)
    {
        app.MapRelayInfoEndpoint();
        app.MapGatewayEndpoints();
        app.MapRendezvousEndpoints();
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
