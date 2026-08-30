namespace HumanGateway.Core.Artifacts;

/// <summary>
/// Content-addressed artifact byte store port (ARTF-FR-01, PROTO-FR-04). Bytes are addressed by their SHA-256
/// content hash (<c>sha256:&lt;hex&gt;</c>); equal content yields an equal address, so duplicate uploads are
/// deduplicated without re-transfer. The Edge owns the filesystem implementation (content-hash-named files,
/// LOCAL-EDGE-1.5); the Relay owns the PostgreSQL BYTEA implementation. This port stores only bytes — artifact
/// metadata lives in the durable schema and is correlated by ID + hash.
/// </summary>
public interface IArtifactStore
{
    /// <summary>
    /// Durably stores <paramref name="content"/>, verifying it matches <paramref name="expectedHash"/> as it is
    /// written (tamper/corruption detection, SP-06). Returns <c>true</c> when bytes were newly written, or
    /// <c>false</c> when identical bytes were already present (dedup — no rewrite). Throws
    /// <see cref="ArtifactHashMismatchException"/> when the content hash does not match, and
    /// <see cref="FormatException"/> when <paramref name="expectedHash"/> is not a well-formed content hash.
    /// </summary>
    Task<bool> SaveAsync(Stream content, string expectedHash, CancellationToken ct = default);

    /// <summary>Opens a read stream over the stored bytes, or returns <c>null</c> when absent.</summary>
    Task<Stream?> OpenReadAsync(string hash, CancellationToken ct = default);

    /// <summary>Returns <c>true</c> when bytes for the given hash are present.</summary>
    Task<bool> ExistsAsync(string hash, CancellationToken ct = default);

    /// <summary>Removes stored bytes for the hash; a no-op when absent.</summary>
    Task DeleteAsync(string hash, CancellationToken ct = default);
}
