namespace HumanGateway.Core.Artifacts;

/// <summary>
/// The Edge → Relay artifact byte channel (ARTF-FR-01, PROTO-FR-04 exception: "only the sync channel's
/// artifact chunk transfer moves bytes"). Content-addressed, deduplicated, and resumable: the caller checks
/// which hashes the remote already holds, uploads/downloads only the missing bytes, and every transfer ends
/// with a content-hash verification on the receiving side (SP-06).
/// </summary>
public interface IArtifactTransfer
{
    /// <summary>
    /// True when a remote artifact service is configured. When false the worker skips artifact transfer
    /// entirely (offline-first, NF-01) and inbound references simply await a later sync cycle.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Returns the subset of <paramref name="hashes"/> the remote side does <em>not</em> hold (dedup
    /// ARTF-FR-01): transfer only what is missing. Identical hashes are de-duplicated in the result.
    /// </summary>
    Task<IReadOnlyList<string>> CheckHashesAsync(IReadOnlyCollection<string> hashes, CancellationToken ct = default);

    /// <summary>
    /// Uploads <paramref name="content"/> (whose bytes hash to <paramref name="hash"/> of
    /// <paramref name="sizeBytes"/>) in resumable chunks (ARTF-FR-02). Deduplicated: the remote skips
    /// identical content it already holds, and a transfer interrupted mid-way resumes from the last completed
    /// chunk. Throws <see cref="ArtifactHashMismatchException"/> when the completed upload fails the remote's
    /// content-hash verification.
    /// </summary>
    Task UploadAsync(string hash, long sizeBytes, Stream content, CancellationToken ct = default);

    /// <summary>
    /// The remote byte size of <paramref name="hash"/>, or <see langword="null"/> when the remote does not
    /// hold it. A receiver calls this to decide whether a pull-side reference can be satisfied yet.
    /// </summary>
    Task<long?> GetRemoteSizeAsync(string hash, CancellationToken ct = default);

    /// <summary>
    /// Downloads <paramref name="hash"/> into <paramref name="sink"/>, a resumable stream positioned at the
    /// number of bytes already persisted locally (a partial temp file); the transfer resumes from that length
    /// via a Range read (ARTF-FR-02). Returns the total bytes held after the copy. Throws
    /// <see cref="ArtifactNotFoundException"/> when the remote does not hold the hash.
    /// </summary>
    Task<long> DownloadAsync(string hash, Stream sink, CancellationToken ct = default);
}
