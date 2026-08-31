using System.Text;
using HumanGateway.Core.Artifacts;
using HumanGateway.Core.Hashing;
using HumanGateway.Relay.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace HumanGateway.Relay.Tests;

/// <summary>
/// Store tests for the Relay's PostgreSQL BYTEA artifact store (CLOUD-RELAY-4.5, RELAY-FR-01, ARTF-FR-01):
/// content-addressed rows, deduplication of identical content, hash verification (SP-06), malformed-hash
/// rejection, and streaming reads (cloud-relay §6: "Relay store logic ... xUnit with test DB").
/// Runs against the real PostgreSQL container (PostgresRelayFixture) with the schema applied via EF Core
/// migrations — the same schema Program.cs migrates at startup.
/// </summary>
public sealed class PostgresArtifactStoreTests : IClassFixture<PostgresRelayFixture>
{
    private static readonly object SchemaLock = new();
    private static bool _schemaApplied;

    private readonly PostgresRelayFixture _fixture;
    private readonly IDbContextFactory<RelayDbContext> _factory;

    public PostgresArtifactStoreTests(PostgresRelayFixture fixture)
    {
        _fixture = fixture;
        _factory = CreateFactory();

        // Materialise the schema once per test class (migrations are idempotent; later tests no-op).
        if (!_schemaApplied)
        {
            lock (SchemaLock)
            {
                if (!_schemaApplied)
                {
                    using var db = _factory.CreateDbContext();
                    db.Database.Migrate();
                    _schemaApplied = true;
                }
            }
        }
    }

    private IDbContextFactory<RelayDbContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<RelayDbContext>()
            .UseNpgsql(_fixture.Container.GetConnectionString())
            .Options;
        return new PooledDbContextFactory<RelayDbContext>(options);
    }

    private PostgresArtifactStore Store() => new(_factory);

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Hash(byte[] bytes) => ContentHasher.Compute(bytes);

    /// <summary>Counts stored rows for a specific content hash (the shared per-class database also holds
    /// rows written by other tests, so assertions must be scoped to the hash under test).</summary>
    private async Task<long> CountRowsAsync(string hash, CancellationToken ct = default)
    {
        var key = "sha256:" + ArtifactHash.RequireHex(hash);
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ArtifactBlobs.AsNoTracking().CountAsync(e => e.Hash == key, ct);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken ct = default)
    {
        await using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    // -----------------------------------------------------------------------------------------------
    // Content-addressed BYTEA round-trip (artifacts §6 #1, ARTF-FR-01)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Save_WritesBtyeaRow_AndReadsBackIntact()
    {
        var store = Store();
        var bytes = Bytes("hello relay artifact bytes");
        var hash = Hash(bytes);

        var wrote = await store.SaveAsync(new MemoryStream(bytes), hash);

        Assert.True(wrote);
        Assert.True(await store.ExistsAsync(hash));

        await using var read = await store.OpenReadAsync(hash);
        Assert.NotNull(read);
        Assert.Equal(bytes, await ReadAllAsync(read!));
    }

    [Fact]
    public async Task Save_HashHexIsCaseInsensitive_NormalisesToLowercase()
    {
        var store = Store();
        var bytes = Bytes("case-insensitive hex");
        var hash = Hash(bytes);
        // The prefix is the canonical "sha256:" token; the hex digits are accepted case-insensitively
        // and normalised to lowercase when addressing the store.
        var uppercaseHex = "sha256:" + hash[ContentHasher.AlgorithmPrefix.Length..].ToUpperInvariant();

        Assert.True(await store.SaveAsync(new MemoryStream(bytes), uppercaseHex));
        Assert.True(await store.ExistsAsync(hash));
        Assert.True(await store.ExistsAsync(uppercaseHex));

        await using var read = await store.OpenReadAsync(uppercaseHex);
        Assert.Equal(bytes, await ReadAllAsync(read!));
    }

    [Fact]
    public async Task Save_AndRead_EmptyContent()
    {
        var store = Store();
        var hash = Hash(Array.Empty<byte>());

        Assert.True(await store.SaveAsync(new MemoryStream(), hash));
        Assert.True(await store.ExistsAsync(hash));

        await using var read = await store.OpenReadAsync(hash);
        Assert.Equal(Array.Empty<byte>(), await ReadAllAsync(read!));
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
        Assert.Equal(1, await CountRowsAsync(hash));
    }

    [Fact]
    public async Task Save_ConcurrentIdenticalContent_CollapsesToSingleRow()
    {
        var store = Store();
        var bytes = Bytes("concurrent identical bytes");
        var hash = Hash(bytes);

        const int writers = 32;
        var results = await Task.WhenAll(
            Enumerable.Range(0, writers).Select(_ => store.SaveAsync(new MemoryStream(bytes), hash)));

        // Exactly one writer wins the insert; the rest observe the winner (fast dedup or PK conflict) and
        // report dedup — one durable row, no duplicates.
        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(writers - 1, results.Count(r => !r));
        Assert.Equal(1, await CountRowsAsync(hash));

        await using var read = await store.OpenReadAsync(hash);
        Assert.Equal(bytes, await ReadAllAsync(read!));
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
        // The tampered content was never stored — neither under the declared hash nor the actual one.
        Assert.Equal(0, await CountRowsAsync(wrongHash));
        Assert.Equal(0, await CountRowsAsync(Hash(bytes)));
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
    // Streaming reads (RELAY-FR-01: BYTEA with streaming reads)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task OpenRead_ReturnsANonSeekableStreamingStream()
    {
        var store = Store();
        var bytes = Bytes("streaming read");
        var hash = Hash(bytes);
        await store.SaveAsync(new MemoryStream(bytes), hash);

        await using var read = await store.OpenReadAsync(hash);
        Assert.NotNull(read);
        // The stream reads from the PostgreSQL wire — it is forward-only, never buffered as a byte[].
        Assert.True(read!.CanRead);
        Assert.False(read.CanSeek);
        Assert.False(read.CanWrite);

        // Exercise the chunked async read path (Memory<byte> and byte[] overloads).
        var buffer = new byte[bytes.Length];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var n = await read.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset));
            if (n == 0)
            {
                break;
            }

            offset += n;
        }

        Assert.Equal(bytes.Length, offset);
        Assert.Equal(bytes, buffer);
        Assert.Equal(0, await read.ReadAsync(new byte[1].AsMemory()));
    }

    [Fact]
    public async Task LargeArtifact_RoundTripsIntact_AndStreams()
    {
        var store = Store();

        // Deterministic ~2 MiB payload: large enough to span many PostgreSQL wire buffers, exercising the
        // sequential streaming read end to end.
        var bytes = new byte[2 * 1024 * 1024 + 7];
        var random = new Random(42);
        random.NextBytes(bytes);
        var hash = Hash(bytes);

        Assert.True(await store.SaveAsync(new MemoryStream(bytes), hash));
        Assert.Equal(1, await CountRowsAsync(hash));

        await using var read = await store.OpenReadAsync(hash);
        var roundTripped = await ReadAllAsync(read!);
        Assert.Equal(bytes, roundTripped);

        // A second store opened over the same data is a dedup, not a rewrite.
        Assert.False(await store.SaveAsync(new MemoryStream(bytes), hash));
        Assert.Equal(1, await CountRowsAsync(hash));
    }
}
