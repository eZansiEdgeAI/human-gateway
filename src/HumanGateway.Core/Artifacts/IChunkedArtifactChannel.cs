namespace HumanGateway.Core.Artifacts;

/// <summary>
/// The low-level, offset-addressed channel a resumable chunked transfer runs over (ARTF-FR-02). The HTTP
/// transport implements it against the Relay's artifact endpoints; tests implement it in memory. Chunks are
/// idempotent by <c>(hash, offset)</c>, so replaying a chunk is harmless and an interrupted transfer resumes
/// from <see cref="GetResumeOffsetAsync"/> — the number of bytes the receiving side has durably accepted.
/// </summary>
public interface IChunkedArtifactChannel
{
    /// <summary>
    /// The number of bytes the receiving side already holds for <paramref name="hash"/> (0 when nothing; the
    /// full size when the content is already complete). The upload resumes from this offset.
    /// </summary>
    Task<long> GetResumeOffsetAsync(string hash, CancellationToken ct = default);

    /// <summary>
    /// Delivers one chunk of <paramref name="chunk"/> that must land at byte <paramref name="offset"/>.
    /// Idempotent per (hash, offset). Throws when the offset does not match the receiving side's expectation
    /// (concurrent writer / out-of-order delivery) so the caller re-queries the resume offset.
    /// </summary>
    Task SendChunkAsync(string hash, long offset, ReadOnlyMemory<byte> chunk, CancellationToken ct = default);

    /// <summary>
    /// Finalises the upload: the receiving side verifies the accumulated bytes against the declared content
    /// hash and publishes them durably. Throws <see cref="ArtifactHashMismatchException"/> on corruption.
    /// </summary>
    Task CompleteAsync(string hash, CancellationToken ct = default);

    /// <summary>The remote byte size of <paramref name="hash"/>, or -1 when the remote does not hold it.</summary>
    Task<long> GetSizeAsync(string hash, CancellationToken ct = default);

    /// <summary>
    /// Opens a read stream over the remote bytes starting at byte <paramref name="offset"/> (a Range request),
    /// or returns <see langword="null"/> when the remote does not hold <paramref name="hash"/>.
    /// </summary>
    Task<Stream?> OpenRangeAsync(string hash, long offset, CancellationToken ct = default);
}
