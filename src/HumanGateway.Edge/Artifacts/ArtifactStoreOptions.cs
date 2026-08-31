namespace HumanGateway.Edge.Artifacts;

using HumanGateway.Core.Artifacts;

/// <summary>Configuration for the Edge local filesystem artifact store (LOCAL-EDGE-1.5, ARTF-FR-03).</summary>
public sealed class ArtifactStoreOptions
{
    public const string SectionName = "Artifacts";

    /// <summary>
    /// Root directory for content-addressed artifact bytes. When null/empty, the service falls back to
    /// <c>&lt;ContentRoot&gt;/data/artifacts</c> (see Program.cs).
    /// </summary>
    public string? RootPath { get; init; }

    /// <summary>
    /// Maximum size of a single artifact accepted by the local upload endpoint, in bytes (ARTF-FR-03,
    /// product vision Open Q #7). Default 50 MiB; per-gateway configurable.
    /// </summary>
    public long MaxArtifactSizeBytes { get; init; } = ArtifactLimits.DefaultMaxArtifactSizeBytes;

    /// <summary>
    /// Per-gateway storage quota: the sum of registered artifact sizes may not exceed this, in bytes
    /// (ARTF-FR-03). Deduplicated content (identical bytes already stored) is not counted again. Default 1 GiB.
    /// </summary>
    public long QuotaBytes { get; init; } = ArtifactLimits.DefaultQuotaBytes;

    /// <summary>
    /// Chunk size framing resumable artifact transfers between this Edge and the Relay (ARTF-FR-02,
    /// artifacts Open Q #1). Default 4 MiB.
    /// </summary>
    public int ChunkSizeBytes { get; init; } = ArtifactLimits.DefaultChunkSizeBytes;
}
