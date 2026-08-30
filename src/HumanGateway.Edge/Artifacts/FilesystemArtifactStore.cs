using System.Security.Cryptography;
using HumanGateway.Core.Artifacts;

namespace HumanGateway.Edge.Artifacts;

/// <summary>
/// Local filesystem <see cref="IArtifactStore"/> (LOCAL-EDGE-1.5, EDGE-FR-02, ARTF-FR-01): bytes are stored
/// content-addressed, named by their SHA-256 hex digest and sharded into two-level subdirectories
/// (<c>&lt;root&gt;/&lt;aa&gt;/&lt;bb&gt;/&lt;64-hex&gt;</c>). Equal content always lands on the same path, so duplicate
/// uploads are deduplicated with no re-transfer, and the address is verified against the bytes as they are
/// written (tamper/corruption detection, SP-06).
/// </summary>
/// <remarks>
/// Writes are atomic and crash-safe: bytes are streamed to a temp file in the destination directory, hashed
/// incrementally, flushed to disk, then renamed into place (<see cref="File.Move(string,string,bool)"/> with
/// <c>overwrite: false</c>). A partial write therefore never appears at a content-addressed path — a killed
/// process leaves only an orphaned temp file, never a corrupt artifact. Concurrent saves of identical content
/// race to the same path and collapse to a single file (the loser observes the winner's rename and reports
/// dedup).
/// </remarks>
public sealed class FilesystemArtifactStore : IArtifactStore
{
    private const int BufferSize = 64 * 1024;

    private readonly string _root;

    /// <summary>Creates the store rooted at <paramref name="root"/> (created on demand).</summary>
    public FilesystemArtifactStore(string root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <inheritdoc />
    public async Task<bool> SaveAsync(Stream content, string expectedHash, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Reject a malformed hash before any I/O; well-formedness is a precondition of content addressing.
        var hex = ArtifactHash.RequireHex(expectedHash);
        var finalPath = PathFor(hex);

        // Fast dedup check — identical content already present, nothing to do (no rewrite, no re-transfer).
        if (File.Exists(finalPath))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(directory);

        // Write to a sibling temp file so the final publish is an atomic same-volume rename.
        var tempPath = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var actualHex = await WriteTempAsync(content, tempPath, ct).ConfigureAwait(false);
            if (!string.Equals(actualHex, hex, StringComparison.Ordinal))
            {
                throw new ArtifactHashMismatchException(expectedHash, ContentHashPrefix + actualHex);
            }

            try
            {
                File.Move(tempPath, finalPath, overwrite: false);
                return true;
            }
            catch (IOException) when (File.Exists(finalPath))
            {
                // A concurrent writer published identical bytes first — dedup, discard our copy.
                return false;
            }
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(string hash, CancellationToken ct = default)
    {
        var path = PathFor(ArtifactHash.RequireHex(hash));
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        // FileShare.Read permits concurrent readers (and a concurrent delete on Windows fails harmlessly).
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<Stream?>(stream);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string hash, CancellationToken ct = default)
    {
        var path = PathFor(ArtifactHash.RequireHex(hash));
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(path));
    }

    /// <inheritdoc />
    public Task DeleteAsync(string hash, CancellationToken ct = default)
    {
        var path = PathFor(ArtifactHash.RequireHex(hash));
        ct.ThrowIfCancellationRequested();

        // True no-op when absent: File.Delete throws DirectoryNotFoundException if the shard directory has
        // never been created, so guard on existence first. A concurrent reader is unaffected (FileShare.Read).
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    /// <summary>The canonical <c>sha256:</c> prefix used to re-format a computed digest for error reporting.</summary>
    private static string ContentHashPrefix => HumanGateway.Core.Hashing.ContentHasher.AlgorithmPrefix;

    /// <summary>Streams content to <paramref name="tempPath"/>, hashing it incrementally and flushing to disk.</summary>
    private static async Task<string> WriteTempAsync(Stream content, string tempPath, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var output = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[BufferSize];
            int read;
            while ((read = await content.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }

            // Flush to stable storage before the rename, so a published artifact is never a torn write.
            output.Flush(flushToDisk: true);
        }

        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>Maps a lowercase hex digest to its sharded path (<c>&lt;root&gt;/&lt;aa&gt;/&lt;bb&gt;/&lt;hex&gt;</c>).</summary>
    private string PathFor(string hex) =>
        Path.Combine(_root, hex[..2], hex.Substring(2, 2), hex);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup; a leaked .tmp in the shard dir is harmless and never served.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup (Windows file-lock window).
        }
    }
}
