namespace HumanGateway.Edge.Artifacts;

/// <summary>Configuration for the Edge local filesystem artifact store (LOCAL-EDGE-1.5, ARTF-FR-03).</summary>
public sealed class ArtifactStoreOptions
{
    public const string SectionName = "Artifacts";

    /// <summary>
    /// Root directory for content-addressed artifact bytes. When null/empty, the service falls back to
    /// <c>&lt;ContentRoot&gt;/data/artifacts</c> (see Program.cs).
    /// </summary>
    public string? RootPath { get; init; }
}
