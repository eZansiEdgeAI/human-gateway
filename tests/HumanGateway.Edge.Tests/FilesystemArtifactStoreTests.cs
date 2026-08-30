using System.Text;
using HumanGateway.Core.Artifacts;
using HumanGateway.Core.Hashing;
using HumanGateway.Edge.Artifacts;
using Xunit;

namespace HumanGateway.Edge.Tests;

/// <summary>
/// Unit tests for the Edge local filesystem artifact store (LOCAL-EDGE-1.5, ARTF-FR-01): content-hash-named,
/// sharded files, deduplication of identical content, hash verification, atomic writes, and concurrent-save
/// collapse (artifacts feature §6, key scenarios 1-2; EDGE-FR-02/07).
/// </summary>
public sealed class FilesystemArtifactStoreTests : IDisposable
{
    private readonly string _root;

    public FilesystemArtifactStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "hgartifact-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of temp files; a leaked temp dir is harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup (Windows file-lock window).
        }
    }

    private FilesystemArtifactStore Store() => new(_root);

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Hash(byte[] bytes) => ContentHasher.Compute(bytes);

    /// <summary>Returns the expected on-disk path for a hash's lowercase hex digest.</summary>
    private string ShardedPath(string hash)
    {
        var hex = ArtifactHash.RequireHex(hash);
        return Path.Combine(_root, hex[..2], hex.Substring(2, 2), hex);
    }

    private int CountFiles() => Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Count();

    private int CountTempFiles() => Directory.EnumerateFiles(_root, "*.tmp", SearchOption.AllDirectories).Count();

    // -----------------------------------------------------------------------------------------------
    // Content-hash naming + round-trip (artifacts §6 #1)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Save_WritesContentHashNamedFile_AndReadsBackIntact()
    {
        var store = Store();
        var bytes = Bytes("hello artifact bytes");
        var hash = Hash(bytes);

        var wrote = await store.SaveAsync(new MemoryStream(bytes), hash);

        Assert.True(wrote);
        // Bytes are named by the sha256 hex digest, sharded into <aa>/<bb>/ (content-hash naming).
        Assert.True(File.Exists(ShardedPath(hash)));

        await using var read = await store.OpenReadAsync(hash);
        Assert.NotNull(read);
        using var ms = new MemoryStream();
        await read!.CopyToAsync(ms);
        Assert.Equal(bytes, ms.ToArray());
    }

    [Fact]
    public async Task Save_HashHexIsCaseInsensitive_NormalisesToLowercase()
    {
        var store = Store();
        var bytes = Bytes("case");
        var hash = Hash(bytes);
        // The prefix is the canonical "sha256:" token; the hex digits are accepted case-insensitively
        // and normalised to lowercase when addressing the store.
        var uppercaseHex = "sha256:" + hash[ContentHasher.AlgorithmPrefix.Length..].ToUpperInvariant();

        Assert.True(await store.SaveAsync(new MemoryStream(bytes), uppercaseHex));
        Assert.True(await store.ExistsAsync(hash));
        Assert.True(await store.ExistsAsync(uppercaseHex));
        Assert.True(File.Exists(ShardedPath(hash)));
    }

    // -----------------------------------------------------------------------------------------------
    // Deduplication (artifacts §6 #2, ARTF-FR-01)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Save_IdenticalContent_IsDeduplicated()
    {
        var store = Store();
        var bytes = Bytes("deduplicate me");
        var hash = Hash(bytes);

        var first = await store.SaveAsync(new MemoryStream(bytes), hash);
        var second = await store.SaveAsync(new MemoryStream(bytes), hash);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, CountFiles());
    }

    [Fact]
    public async Task Save_ConcurrentIdenticalContent_CollapsesToSingleFile()
    {
        var store = Store();
        var bytes = Bytes("concurrent identical bytes");
        var hash = Hash(bytes);

        const int writers = 32;
        var results = await Task.WhenAll(
            Enumerable.Range(0, writers).Select(_ => store.SaveAsync(new MemoryStream(bytes), hash)));

        // Exactly one writer wins the atomic rename; the rest observe the winner and report dedup.
        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(writers - 1, results.Count(r => !r));
        Assert.Equal(1, CountFiles());

        // The surviving file is intact.
        await using var read = await store.OpenReadAsync(hash);
        using var ms = new MemoryStream();
        await read!.CopyToAsync(ms);
        Assert.Equal(bytes, ms.ToArray());
    }

    // -----------------------------------------------------------------------------------------------
    // Hash verification (SP-06)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Save_HashMismatch_Throws_AndLeavesNoArtifact()
    {
        var store = Store();
        var bytes = Bytes("the actual content");
        var wrongHash = Hash(Bytes("something else"));

        var ex = await Assert.ThrowsAsync<ArtifactHashMismatchException>(
            () => store.SaveAsync(new MemoryStream(bytes), wrongHash));

        Assert.Equal(Hash(bytes), ex.ActualHash);
        Assert.Equal(0, CountFiles());
        Assert.Equal(0, CountTempFiles());
    }

    [Fact]
    public async Task Save_MalformedHash_ThrowsFormatException()
    {
        var store = Store();
        var bytes = Bytes("any bytes");

        await Assert.ThrowsAsync<FormatException>(
            () => store.SaveAsync(new MemoryStream(bytes), "not-a-hash"));
        await Assert.ThrowsAsync<FormatException>(
            () => store.SaveAsync(new MemoryStream(bytes), "sha256:tooshort"));
        await Assert.ThrowsAsync<FormatException>(
            () => store.SaveAsync(new MemoryStream(bytes), "md5:" + new string('a', 32)));
        // Non-canonical algorithm prefixes are rejected too (only the lowercase "sha256:" token is valid).
        await Assert.ThrowsAsync<FormatException>(
            () => store.SaveAsync(new MemoryStream(bytes), "SHA256:" + new string('a', 64)));
    }

    // -----------------------------------------------------------------------------------------------
    // Exists / Delete
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Exists_And_OpenRead_ReturnFalseNullWhenAbsent()
    {
        var store = Store();
        var absent = Hash(Bytes("never stored"));

        Assert.False(await store.ExistsAsync(absent));
        Assert.Null(await store.OpenReadAsync(absent));
    }

    [Fact]
    public async Task Delete_RemovesBytes_AndIsNoOpWhenAbsent()
    {
        var store = Store();
        var bytes = Bytes("delete me");
        var hash = Hash(bytes);

        await store.SaveAsync(new MemoryStream(bytes), hash);
        Assert.True(await store.ExistsAsync(hash));

        await store.DeleteAsync(hash);
        Assert.False(await store.ExistsAsync(hash));

        // Deleting a hash that is not present is a no-op, not an error.
        await store.DeleteAsync(hash);
        await store.DeleteAsync(Hash(Bytes("also never stored")));
    }

    // -----------------------------------------------------------------------------------------------
    // Atomic write (EDGE-FR-07)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Save_LeavesNoTempFiles_AfterSuccess()
    {
        var store = Store();

        for (var i = 0; i < 5; i++)
        {
            await store.SaveAsync(new MemoryStream(Bytes($"content-{i}")), Hash(Bytes($"content-{i}")));
        }

        Assert.Equal(5, CountFiles());
        Assert.Equal(0, CountTempFiles());
    }
}
