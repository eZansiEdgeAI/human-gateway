namespace HumanGateway.Relay.Options;

/// <summary>
/// Relay behaviour options, read from the <c>Relay</c> configuration section. Sensible defaults keep the
/// service working out of the box; deployments override via environment variables (e.g.
/// <c>Relay__RegistrationTokenTtlDays</c>, <c>Relay__Rendezvous__OnlineWindowMinutes</c>).
/// </summary>
public sealed class RelayOptions
{
    /// <summary>The configuration section this options type binds to.</summary>
    public const string SectionName = "Relay";

    /// <summary>Lifetime of a freshly issued registration token before the Edge must rotate (AUTH-FR-01).</summary>
    public int RegistrationTokenTtlDays { get; set; } = 30;

    /// <summary>Rendezvous routing behaviour (WEBX-FR-02).</summary>
    public RendezvousOptions Rendezvous { get; set; } = new();

    /// <summary>Sync endpoint behaviour (RELAY-FR-02, SYNC-FR-03).</summary>
    public SyncOptions Sync { get; set; } = new();
}

/// <summary>Sync endpoint behaviour options, bound from <c>Relay:Sync</c>.</summary>
public sealed class SyncOptions
{
    /// <summary>Maximum items the Relay includes in one PULL response batch (schema cap is 1000).</summary>
    public int PullBatchSize { get; set; } = 1000;
}

/// <summary>Rendezvous behaviour options, bound from <c>Relay:Rendezvous</c>.</summary>
public sealed class RendezvousOptions
{
    /// <summary>How recent a gateway's <c>lastSeenAt</c> must be to count as "online" for rendezvous routing.</summary>
    public int OnlineWindowMinutes { get; set; } = 15;
}
