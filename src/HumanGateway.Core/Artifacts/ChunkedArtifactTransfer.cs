namespace HumanGateway.Core.Artifacts;

/// <summary>
/// Transport-agnostic driver for resumable, chunked artifact transfer (ARTF-FR-02). Uploads stream the source
/// in fixed-size chunks through an <see cref="IChunkedArtifactChannel"/>; a transfer interrupted mid-way
/// resumes from the receiving side's durably accepted offset (chunks are idempotent by offset, and the driver
/// seeks the source past bytes already delivered). Downloads stream the remote bytes into a resumable sink,
/// resuming from the sink's current length via a Range read. Both directions finish with a receiving-side
/// content-hash verification (SP-06), so an interrupted-and-resumed transfer is never trusted until its hash
/// checks out.
/// </summary>
public static class ChunkedArtifactTransfer
{
    /// <summary>
    /// Uploads <paramref name="source"/> (a seekable stream whose bytes hash to <paramref name="hash"/>) to
    /// <paramref name="channel"/> in <paramref name="chunkSizeBytes"/> chunks, resuming from whatever the
    /// channel already holds. Returns the total number of bytes the channel holds when complete (the full
    /// artifact size). The source's hash must equal <paramref name="hash"/> — the channel verifies the
    /// accumulated bytes on <see cref="IChunkedArtifactChannel.CompleteAsync"/> and throws
    /// <see cref="ArtifactHashMismatchException"/> on mismatch.
    /// </summary>
    public static async Task<long> UploadAsync(
        IChunkedArtifactChannel channel,
        Stream source,
        string hash,
        int chunkSizeBytes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(source);
        if (chunkSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeBytes), "Chunk size must be positive.");
        }

        var resumeOffset = await channel.GetResumeOffsetAsync(hash, ct).ConfigureAwait(false);
        if (resumeOffset > source.Length)
        {
            // The remote holds more bytes than this local source — the content was replaced or the hashes
            // collide. Do not clobber: surface the inconsistency rather than corrupting the remote store.
            throw new InvalidOperationException(
                $"Resume offset {resumeOffset} exceeds the local artifact size {source.Length} for '{hash}'.");
        }

        if (source.CanSeek)
        {
            source.Seek(resumeOffset, SeekOrigin.Begin);
        }

        var buffer = new byte[chunkSizeBytes];
        var sent = resumeOffset;
        while (sent < source.Length)
        {
            var count = (int)Math.Min(chunkSizeBytes, source.Length - sent);
            var read = await source.ReadAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break; // source shrank mid-transfer; the completion hash check will reject it
            }

            try
            {
                await channel.SendChunkAsync(hash, sent, buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                sent += read;
            }
            catch (ChunkOffsetMismatchException)
            {
                // The receiver's partial state diverged (concurrent writer or a reset). Re-sync to its
                // authoritative offset and continue — chunks are idempotent by offset, so nothing is lost.
                sent = await channel.GetResumeOffsetAsync(hash, ct).ConfigureAwait(false);
                if (source.CanSeek)
                {
                    source.Seek(sent, SeekOrigin.Begin);
                }
            }
        }

        await channel.CompleteAsync(hash, ct).ConfigureAwait(false);
        return sent;
    }

    /// <summary>
    /// Downloads <paramref name="hash"/> from <paramref name="channel"/> into <paramref name="sink"/>, a
    /// resumable sink whose <see cref="Stream.Length"/> is the number of bytes already persisted locally
    /// (the transport positions a partial temp file before calling). Resumes via a Range read from that
    /// length; returns the total bytes written. Throws <see cref="ArtifactNotFoundException"/> when the remote
    /// does not hold the hash.
    /// </summary>
    public static async Task<long> DownloadAsync(
        IChunkedArtifactChannel channel,
        Stream sink,
        string hash,
        int chunkSizeBytes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(sink);
        if (chunkSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeBytes), "Chunk size must be positive.");
        }

        var remoteSize = await channel.GetSizeAsync(hash, ct).ConfigureAwait(false);
        if (remoteSize < 0)
        {
            throw new ArtifactNotFoundException(hash);
        }

        var offset = sink.Length;
        if (offset > remoteSize)
        {
            throw new InvalidOperationException(
                $"Local download of '{hash}' already holds {offset} bytes but the remote has {remoteSize}.");
        }

        if (offset == remoteSize)
        {
            return offset; // already fully downloaded locally
        }

        await using (var source = await channel.OpenRangeAsync(hash, offset, ct).ConfigureAwait(false)
            ?? throw new ArtifactNotFoundException(hash))
        {
            var buffer = new byte[chunkSizeBytes];
            while ((await source.ReadAsync(buffer, ct).ConfigureAwait(false)) is { } read and > 0)
            {
                await sink.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                offset += read;
            }
        }

        return offset;
    }
}
