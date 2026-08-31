using HumanGateway.Core.Artifacts;

namespace HumanGateway.Edge.Sync;

/// <summary>
/// Configuration for the Edge's outbound artifact-byte channel to the Relay (ARTF-FR-01, PROTO-FR-04
/// exception), bound from the <c>Relay</c> configuration section. The byte channel is outbound-only (SP-01)
/// and separate from the sync-batch channel — the batch transport is provided by the synchronisation feature.
/// </summary>
public sealed class RelayArtifactOptions
{
    public const string SectionName = "Relay";

    /// <summary>
    /// Base URL of the Relay's HTTP API (e.g. <c>https://relay.example.com</c>). When null/empty the channel
    /// is disabled and the Edge stays offline-first for artifact bytes (NF-01).
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// Chunk size framing resumable uploads to the Relay (ARTF-FR-02, artifacts Open Q #1). Default 4 MiB.
    /// </summary>
    public int ChunkSizeBytes { get; init; } = ArtifactLimits.DefaultChunkSizeBytes;

    /// <summary>True when a Relay base URL is configured.</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(BaseUrl);
}
