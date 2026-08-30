namespace HumanGateway.Core.Artifacts;

/// <summary>
/// Raised when stored artifact bytes do not match the declared content hash (ARTF-FR-01, SP-06). Carries both
/// hashes so callers can produce a precise, machine-readable error without leaking bytes.
/// </summary>
public sealed class ArtifactHashMismatchException : Exception
{
    /// <summary>The hash the caller declared (as received).</summary>
    public string DeclaredHash { get; }

    /// <summary>The hash computed over the actual bytes (<c>sha256:&lt;hex&gt;</c>).</summary>
    public string ActualHash { get; }

    /// <summary>Creates the exception for a declared/actual hash pair.</summary>
    public ArtifactHashMismatchException(string declaredHash, string actualHash)
        : base($"Artifact content hash mismatch: declared '{declaredHash}' but computed '{actualHash}'.")
    {
        DeclaredHash = declaredHash;
        ActualHash = actualHash;
    }
}
