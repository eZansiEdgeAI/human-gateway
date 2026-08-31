namespace HumanGateway.Core.Artifacts;

/// <summary>
/// Raised when a download target is addressed by a content hash the remote side does not hold (ARTF-FR-01).
/// Callers treat it as "skip and retry later" — the remote gateway may not have uploaded the bytes yet.
/// </summary>
public sealed class ArtifactNotFoundException : Exception
{
    /// <summary>Creates the exception for a missing content hash.</summary>
    public ArtifactNotFoundException(string hash)
        : base($"Artifact content for hash '{hash}' is not available on the remote side.")
    {
    }
}
