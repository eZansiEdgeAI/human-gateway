namespace HumanGateway.Core.Artifacts;

/// <summary>
/// Raised by a chunked channel when a chunk's offset does not match the receiving side's expected position
/// (concurrent writer, or the receiver lost/reset its partial state). The resumable upload driver catches it
/// and re-queries the receiving side's authoritative offset before continuing (ARTF-FR-02).
/// </summary>
public sealed class ChunkOffsetMismatchException : Exception
{
    /// <summary>Creates the exception for a rejected chunk at <paramref name="offset"/>.</summary>
    public ChunkOffsetMismatchException(long offset, string hash)
        : base($"The receiving side rejected the chunk at offset {offset} for '{hash}' (offset mismatch).")
    {
    }
}
