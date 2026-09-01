namespace HumanGateway.Relay.Options;

using HumanGateway.Core.Artifacts;

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

    /// <summary>
    /// Maximum clock skew (minutes) the Relay tolerates for the request-signature timestamp (AUTH-FR-04).
    /// A signed request whose <c>X-Hg-Timestamp</c> is older (or further ahead) than this window is rejected as
    /// stale, bounding the replay window for a captured signed request. Default 5 minutes.
    /// </summary>
    public int RequestSignatureSkewMinutes { get; set; } = 5;

    /// <summary>Rendezvous routing behaviour (WEBX-FR-02).</summary>
    public RendezvousOptions Rendezvous { get; set; } = new();

    /// <summary>Sync endpoint behaviour (RELAY-FR-02, SYNC-FR-03).</summary>
    public SyncOptions Sync { get; set; } = new();

    /// <summary>Artifact byte-channel behaviour (RELAY-FR-01, ARTF-FR-02/03).</summary>
    public RelayArtifactOptions Artifacts { get; set; } = new();
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

/// <summary>
/// Relay artifact byte-channel behaviour, bound from <c>Relay:Artifacts</c> (ARTF-FR-02/03). These are the
/// Relay's own configurable limits — each Edge gateway enforces its own per-gateway limits locally too.
/// </summary>
public sealed class RelayArtifactOptions
{
    /// <summary>
    /// Maximum size of a single artifact the Relay accepts, in bytes (product vision Open Q #7: 50 MiB
    /// default, configurable per gateway).
    /// </summary>
    public long MaxArtifactSizeBytes { get; set; } = ArtifactLimits.DefaultMaxArtifactSizeBytes;

    /// <summary>
    /// Total BYTEA blob-store budget, in bytes (ARTF-FR-03). Deduplicated content counts once — the sum of
    /// <c>artifact_blobs.size_bytes</c> may not exceed this. Default 1 GiB.
    /// </summary>
    public long QuotaBytes { get; set; } = ArtifactLimits.DefaultQuotaBytes;

    /// <summary>Chunk size framing resumable transfers (artifacts Open Q #1: 4 MiB default).</summary>
    public int ChunkSizeBytes { get; set; } = ArtifactLimits.DefaultChunkSizeBytes;
}
