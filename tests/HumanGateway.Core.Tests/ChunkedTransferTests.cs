using System.Text;
using HumanGateway.Core.Artifacts;
using HumanGateway.Core.Hashing;
using Xunit;

namespace HumanGateway.Core.Tests;

/// <summary>
/// Tests for the resumable chunked-transfer primitives (ARTF-FR-02, artifacts §6): deterministic chunk
/// framing, the upload driver resuming from a mid-way interruption (the chaos scenario "kill transfer
/// mid-way; resume; verify integrity"), the download driver resuming via a Range read, and self-healing
/// when the receiver's offset state diverges.
/// </summary>
public sealed class ChunkedTransferTests
{
    private const int ChunkSize = 4 * 1024 * 1024;

    // ------------------------------------------------------------------------------------------------
    // ArtifactChunking — deterministic framing
    // ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(ChunkSize - 1, 1)]
    [InlineData(ChunkSize, 1)]
    [InlineData(ChunkSize + 1, 2)]
    [InlineData(3 * ChunkSize, 3)]
    [InlineData(3 * ChunkSize + 123, 4)]
    public void ChunkCount_CoversSizeInChunkSizedBlocks(long sizeBytes, int expectedChunks)
    {
        Assert.Equal(expectedChunks, ArtifactChunking.ChunkCount(sizeBytes, ChunkSize));
    }

    [Fact]
    public void ChunkRange_PartitionsTheArtifactContiguously()
    {
        const long size = 2 * ChunkSize + 17;
        var count = ArtifactChunking.ChunkCount(size, ChunkSize);
        Assert.Equal(3, count);

        var (o0, l0) = ArtifactChunking.ChunkRange(size, ChunkSize, 0);
        var (o1, l1) = ArtifactChunking.ChunkRange(size, ChunkSize, 1);
        var (o2, l2) = ArtifactChunking.ChunkRange(size, ChunkSize, 2);

        Assert.Equal(0, o0);
        Assert.Equal(ChunkSize, l0);
        Assert.Equal(ChunkSize, o1);
        Assert.Equal(ChunkSize, l1);
        Assert.Equal(2 * ChunkSize, o2);
        Assert.Equal(17, l2); // the final chunk is short

        // The partition covers every byte exactly once, in order.
        long covered = 0;
        for (var i = 0; i < count; i++)
        {
            var (offset, length) = ArtifactChunking.ChunkRange(size, ChunkSize, i);
            Assert.Equal(covered, offset);
            covered += length;
        }

        Assert.Equal(size, covered);
    }

    [Fact]
    public void ChunkRange_RejectsOutOfRangeIndex()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ArtifactChunking.ChunkRange(100, ChunkSize, 1));

    // ------------------------------------------------------------------------------------------------
    // Upload: interruption + resume (artifacts §6 #3)
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Upload_InterruptedMidWay_ResumesFromTheReceiverOffset_AndCompletesIntact()
    {
        var content = RandomBytes(2 * ChunkSize + 300);
        var hash = ContentHasher.Compute(content);
        var channel = new InMemoryChunkedChannel();

        // The first attempt fails part-way through the second chunk (e.g. the network dies mid-transfer).
        channel.FailAfterBytes = ChunkSize + 500;
        await Assert.ThrowsAsync<TransferInterruptedException>(() =>
            ChunkedArtifactTransfer.UploadAsync(channel, new MemoryStream(content), hash, ChunkSize, CancellationToken.None));

        // The receiver durably holds a prefix; the retry resumes from exactly that byte, not from zero.
        var resumeOffset = await channel.GetResumeOffsetAsync(hash, CancellationToken.None);
        Assert.Equal(ChunkSize + 500, resumeOffset);

        channel.FailAfterBytes = null;
        await ChunkedArtifactTransfer.UploadAsync(channel, new MemoryStream(content), hash, ChunkSize, CancellationToken.None);

        // The upload completed and the receiving side's hash verification passed (CompleteAsync verified it).
        var received = await channel.GetResumeOffsetAsync(hash, CancellationToken.None);
        Assert.Equal(content.Length, received);
        Assert.Contains(hash, channel.Completed);
        Assert.Equal(content, channel.Published[hash]);
    }

    [Fact]
    public async Task Upload_AlreadyCompleteOnReceiver_IsADedupNoOp()
    {
        var content = RandomBytes(ChunkSize + 10);
        var hash = ContentHasher.Compute(content);
        var channel = new InMemoryChunkedChannel();
        channel.Published[hash] = content; // receiver already holds identical bytes (dedup, ARTF-FR-01)

        var sent = await ChunkedArtifactTransfer.UploadAsync(channel, new MemoryStream(content), hash, ChunkSize, CancellationToken.None);

        Assert.Equal(content.Length, sent);
        Assert.Empty(channel.Chunks); // no chunk bytes crossed the wire
        Assert.Contains(hash, channel.Completed);
    }

    [Fact]
    public async Task Upload_ReceiverOffsetDiverges_SelfHealsToTheReceiverOffset()
    {
        var content = RandomBytes(ChunkSize + 5);
        var hash = ContentHasher.Compute(content);
        var channel = new InMemoryChunkedChannel();

        // The receiver rejects the very first chunk (its state was reset), then accepts everything.
        channel.RejectNextOffset = 0;
        await ChunkedArtifactTransfer.UploadAsync(channel, new MemoryStream(content), hash, ChunkSize, CancellationToken.None);

        Assert.Equal(content.Length, await channel.GetResumeOffsetAsync(hash, CancellationToken.None));
        Assert.Equal(content, channel.Published[hash]);
    }

    // ------------------------------------------------------------------------------------------------
    // Download: Range-based resume
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Download_ResumesFromTheLocalPartialLength_AndReturnsFullBytes()
    {
        var content = RandomBytes(ChunkSize + 200);
        var hash = ContentHasher.Compute(content);
        var channel = new InMemoryChunkedChannel();
        channel.Published[hash] = content;

        // A local partial already holds the first 64 KiB (an interrupted download).
        var partial = new MemoryStream();
        partial.Write(content.AsSpan(0, 64 * 1024));

        var total = await ChunkedArtifactTransfer.DownloadAsync(channel, partial, hash, ChunkSize, CancellationToken.None);

        Assert.Equal(content.Length, total);
        Assert.Equal(content, partial.ToArray());
        // The range read started at the partial length — only the missing tail crossed the wire.
        Assert.Equal(64 * 1024, channel.RangeReadOffsets[hash]);
    }

    [Fact]
    public async Task Download_RemoteDoesNotHoldHash_ThrowsArtifactNotFound()
    {
        var channel = new InMemoryChunkedChannel();
        var hash = "sha256:" + new string('d', 64);

        await Assert.ThrowsAsync<ArtifactNotFoundException>(() =>
            ChunkedArtifactTransfer.DownloadAsync(channel, new MemoryStream(), hash, ChunkSize, CancellationToken.None));
    }

    // ------------------------------------------------------------------------------------------------
    // Harness
    // ------------------------------------------------------------------------------------------------

    private static byte[] RandomBytes(long count)
    {
        var bytes = new byte[count];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    /// <summary>Raised by the harness to simulate a mid-transfer failure.</summary>
    private sealed class TransferInterruptedException : Exception;

    /// <summary>
    /// In-memory <see cref="IChunkedArtifactChannel"/> that can simulate a mid-transfer interruption
    /// (fail after a byte budget), offset divergence, and the dedup already-complete case.
    /// </summary>
    private sealed class InMemoryChunkedChannel : IChunkedArtifactChannel
    {
        /// <summary>Set to simulate the transfer dying after this many accepted bytes.</summary>
        public long? FailAfterBytes;

        /// <summary>Set to make the receiver reject the chunk at this offset once (offset-divergence scenario).</summary>
        public long? RejectNextOffset;

        public Dictionary<string, byte[]> Published { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, MemoryStream> Partials { get; } = new(StringComparer.Ordinal);

        public List<(string Hash, long Offset, byte[] Chunk)> Chunks { get; } = new();

        public HashSet<string> Completed { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, long> RangeReadOffsets { get; } = new();

        public Task<long> GetResumeOffsetAsync(string hash, CancellationToken ct = default)
        {
            if (Published.TryGetValue(hash, out var published))
            {
                return Task.FromResult((long)published.Length);
            }

            return Task.FromResult(Partials.TryGetValue(hash, out var partial) ? partial.Length : 0);
        }

        public Task SendChunkAsync(string hash, long offset, ReadOnlyMemory<byte> chunk, CancellationToken ct = default)
        {
            if (RejectNextOffset is { } rejected && offset == rejected)
            {
                RejectNextOffset = null;
                throw new ChunkOffsetMismatchException(offset, hash);
            }

            var partial = GetPartial(hash);
            if (partial.Length != offset)
            {
                throw new ChunkOffsetMismatchException(offset, hash);
            }

            Chunks.Add((hash, offset, chunk.ToArray()));

            if (FailAfterBytes is { } budget)
            {
                // Simulate the transfer dying part-way through a chunk: accept only up to the budget, then
                // throw — the partial (a prefix of the content) remains for a resumable retry.
                var remaining = budget - partial.Length;
                if (remaining <= 0)
                {
                    throw new TransferInterruptedException();
                }

                var take = (int)Math.Min(chunk.Length, remaining);
                partial.Write(chunk.Span[..take]);
                if (take < chunk.Length)
                {
                    throw new TransferInterruptedException();
                }
            }
            else
            {
                partial.Write(chunk.Span);
            }

            return Task.CompletedTask;
        }

        public Task CompleteAsync(string hash, CancellationToken ct = default)
        {
            if (Published.ContainsKey(hash))
            {
                // Dedup: the content is already complete — nothing to verify or publish.
                Completed.Add(hash);
                return Task.CompletedTask;
            }

            var bytes = Partials.TryGetValue(hash, out var partial) ? partial.ToArray() : Array.Empty<byte>();
            var actual = ContentHasher.Compute(bytes);
            if (!string.Equals(actual, hash, StringComparison.Ordinal))
            {
                throw new ArtifactHashMismatchException(hash, actual);
            }

            Published[hash] = bytes;
            Partials.Remove(hash);
            Completed.Add(hash);
            return Task.CompletedTask;
        }

        public Task<long> GetSizeAsync(string hash, CancellationToken ct = default)
            => Task.FromResult(Published.TryGetValue(hash, out var bytes) ? (long)bytes.Length : -1);

        public Task<Stream?> OpenRangeAsync(string hash, long offset, CancellationToken ct = default)
        {
            if (!Published.TryGetValue(hash, out var bytes))
            {
                return Task.FromResult<Stream?>(null);
            }

            RangeReadOffsets[hash] = offset;
            return Task.FromResult<Stream?>(new MemoryStream(bytes, (int)offset, bytes.Length - (int)offset));
        }

        private MemoryStream GetPartial(string hash)
        {
            if (!Partials.TryGetValue(hash, out var partial))
            {
                partial = new MemoryStream();
                Partials[hash] = partial;
            }

            return partial;
        }
    }
}
