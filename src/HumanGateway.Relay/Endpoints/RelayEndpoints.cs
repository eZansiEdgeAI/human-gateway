namespace HumanGateway.Relay.Endpoints;

/// <summary>
/// Maps the Cloud Relay HTTP API (RELAY-FR-02..04). The scaffold carries the service-info probe; the
/// gateway registration + rendezvous endpoints (RELAY-FR-03, task CLOUD-RELAY-4.3) and the sync endpoint
/// (push/pull cursors + delivery ack, SYNC-FR-03/05, task CLOUD-RELAY-4.4) land here as their own groups.
/// </summary>
public static class RelayEndpoints
{
    /// <summary>Maps every Relay endpoint onto the app.</summary>
    public static void MapRelayEndpoints(this WebApplication app)
    {
        app.MapRelayInfoEndpoint();
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
