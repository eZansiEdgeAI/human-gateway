namespace HumanGateway.Core.Artifacts;

/// <summary>
/// Shared artifact transfer defaults and chunk-framing math (ARTF-FR-02, ARTF-FR-03, product vision Open Q #7,
/// artifacts Open Q #1). Both Edge and Relay bind these defaults for their configurable per-gateway limits;
/// deployments override via configuration. Keeping the defaults here (Core) means the two sides can never
/// drift on the chunk size that frames resumable transfers.
/// </summary>
public static class ArtifactLimits
{
    /// <summary>Default maximum size of a single artifact: 50 MiB (product vision Open Q #7).</summary>
    public const long DefaultMaxArtifactSizeBytes = 50L * 1024 * 1024;

    /// <summary>Default chunk size for resumable transfer: 4 MiB (artifacts Open Q #1).</summary>
    public const int DefaultChunkSizeBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Default per-gateway storage quota: 1 GiB. The Edge enforces it against its local filesystem store
    /// (sum of registered artifact sizes); the Relay enforces it against its BYTEA blob budget (ARTF-FR-03).
    /// </summary>
    public const long DefaultQuotaBytes = 1024L * 1024 * 1024;

    /// <summary>True when an artifact of <paramref name="sizeBytes"/> exceeds the gateway's configured ceiling.</summary>
    public static bool ExceedsMaxSize(long sizeBytes, long maxBytes)
        => sizeBytes > Math.Max(0, maxBytes);
}

/// <summary>
/// Deterministic chunk framing for resumable artifact transfer (ARTF-FR-02). Both peers derive identical
/// chunk boundaries from <c>(sizeBytes, chunkSizeBytes)</c>, so an interrupted transfer can resume by offset
/// without either side guessing where a chunk starts.
/// </summary>
public static class ArtifactChunking
{
    /// <summary>
    /// The number of chunks covering <paramref name="sizeBytes"/> at <paramref name="chunkSizeBytes"/> each.
    /// Zero bytes → zero chunks (a complete upload of an empty artifact is a single completion handshake).
    /// </summary>
    public static int ChunkCount(long sizeBytes, int chunkSizeBytes)
    {
        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "Artifact size cannot be negative.");
        }

        if (chunkSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeBytes), "Chunk size must be positive.");
        }

        if (sizeBytes == 0)
        {
            return 0;
        }

        return checked((int)((sizeBytes + chunkSizeBytes - 1) / chunkSizeBytes));
    }

    /// <summary>
    /// The byte range of chunk <paramref name="index"/>: <c>(offset, length)</c> within an artifact of
    /// <paramref name="sizeBytes"/> at <paramref name="chunkSizeBytes"/> framing. The final chunk is short.
    /// </summary>
    public static (long Offset, int Length) ChunkRange(long sizeBytes, int chunkSizeBytes, int index)
    {
        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "Artifact size cannot be negative.");
        }

        if (chunkSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeBytes), "Chunk size must be positive.");
        }

        if (index < 0 || index >= ChunkCount(sizeBytes, chunkSizeBytes))
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Chunk index is outside the artifact's chunk range.");
        }

        var offset = (long)index * chunkSizeBytes;
        var length = (int)Math.Min(chunkSizeBytes, sizeBytes - offset);
        return (offset, length);
    }
}
