using System.Data;
using System.Security.Cryptography;
using HumanGateway.Core.Artifacts;
using HumanGateway.Core.Hashing;
using HumanGateway.Core.Time;
using HumanGateway.Relay.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HumanGateway.Relay.Storage;

/// <summary>
/// The Relay's PostgreSQL <see cref="IArtifactStore"/> (RELAY-FR-01, ARTF-FR-01, PROTO-FR-04): content
/// addressed artifact bytes in the <c>artifact_blobs</c> BYTEA table, keyed by the canonical content hash
/// (<c>sha256:&lt;hex&gt;</c>). Equal content yields an equal hash, so one row serves every artifact ID
/// referencing those bytes (dedup — duplicate uploads are not re-written, ARTF-FR-01). This is the Relay
/// counterpart to the Edge's filesystem artifact store; an S3-compatible adapter is an optional later step
/// (cloud-relay Open Q #2), NOT v1.
/// </summary>
/// <remarks>
/// <para><b>Streaming reads.</b> <see cref="OpenReadAsync"/> reads the BYTEA value as a stream directly from
/// the PostgreSQL wire (<see cref="NpgsqlDataReader.GetStream"/> under
/// <see cref="CommandBehavior.SequentialAccess"/>), so a large artifact is never buffered whole into memory.
/// The returned stream owns the reader/command/context that produced it and releases them on disposal.</para>
/// <para><b>Writes verify before storing.</b> The incoming content is streamed through an incremental SHA-256
/// while buffering, verified against the declared hash, and only then inserted (tamper/corruption detection,
/// SP-06). A mismatch throws <see cref="ArtifactHashMismatchException"/> and leaves no row. The dedup fast-path
/// (<c>SELECT ... WHERE hash = @hash</c>) avoids touching content already stored; the primary-key constraint is
/// the hard backstop under concurrent identical uploads (SQLSTATE 23505 → dedup).</para>
/// </remarks>
public sealed class PostgresArtifactStore : IArtifactStore
{
    private const int BufferSize = 64 * 1024;

    private readonly IDbContextFactory<RelayDbContext> _factory;

    /// <summary>Creates the durable BYTEA store over the context factory (short-lived context per operation).</summary>
    public PostgresArtifactStore(IDbContextFactory<RelayDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc />
    public async Task<bool> SaveAsync(Stream content, string expectedHash, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Reject a malformed hash before any I/O; well-formedness is a precondition of content addressing.
        var hex = ArtifactHash.RequireHex(expectedHash);
        var key = ContentHasher.AlgorithmPrefix + hex;

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Fast dedup — identical bytes already stored: nothing to write (no rewrite, no re-transfer).
        if (await db.ArtifactBlobs.AsNoTracking().AnyAsync(e => e.Hash == key, ct).ConfigureAwait(false))
        {
            return false;
        }

        // Stream the content once: hash it incrementally while buffering for the insert. The content is
        // verified BEFORE it is stored, so a tampered upload never leaves a row (SP-06).
        var (data, actualHex) = await BufferAndHashAsync(content, ct).ConfigureAwait(false);
        if (!string.Equals(actualHex, hex, StringComparison.Ordinal))
        {
            throw new ArtifactHashMismatchException(expectedHash, ContentHasher.AlgorithmPrefix + actualHex);
        }

        db.ArtifactBlobs.Add(new ArtifactBlobRecord
        {
            Hash = key,
            Data = data,
            SizeBytes = data.LongLength,
            CreatedAt = ProtocolTime.Now(),
        });

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent writer stored identical bytes first — dedup (ARTF-FR-01). Our copy is discarded.
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Streaming read: the BYTEA value is served from the PostgreSQL wire in chunks
    /// (<see cref="NpgsqlDataReader.GetStream"/>), never materialised as a <see cref="byte"/>[] in Relay
    /// memory. The returned stream keeps the reader/command/context alive until the caller is done and
    /// releases them on disposal.
    /// </remarks>
    public async Task<Stream?> OpenReadAsync(string hash, CancellationToken ct = default)
    {
        var key = ArtifactKey(hash);

        var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        try
        {
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var command = new NpgsqlCommand("SELECT data FROM artifact_blobs WHERE hash = @hash", connection);
            command.Parameters.AddWithValue("hash", key);

            var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                // Absent — release everything and report null.
                await reader.DisposeAsync().ConfigureAwait(false);
                await command.DisposeAsync().ConfigureAwait(false);
                await db.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            // Ownership of the reader/command/context transfers to the stream wrapper so nothing is disposed
            // early while the caller consumes the BYTEA value; the wrapper releases them on its disposal.
            return new NpgsqlByteaStream(reader.GetStream(0), reader, command, db);
        }
        catch
        {
            // Disposing the context closes its connection, which aborts and cleans up any open reader/command.
            await db.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string hash, CancellationToken ct = default)
    {
        var key = ArtifactKey(hash);

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.ArtifactBlobs.AsNoTracking().AnyAsync(e => e.Hash == key, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>Deletes in SQL (EF <c>ExecuteDeleteAsync</c>) — a true no-op when the hash is absent, with no
    /// row ever loaded into memory.</remarks>
    public async Task DeleteAsync(string hash, CancellationToken ct = default)
    {
        var key = ArtifactKey(hash);

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await db.ArtifactBlobs.Where(e => e.Hash == key).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    /// <summary>Normalises a content hash to its canonical <c>sha256:&lt;lowercase hex&gt;</c> storage key.</summary>
    private static string ArtifactKey(string hash)
        => ContentHasher.AlgorithmPrefix + ArtifactHash.RequireHex(hash);

    /// <summary>Streams content once, hashing incrementally and buffering for the insert.</summary>
    private static async Task<(byte[] Data, string Hex)> BufferAndHashAsync(Stream content, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var buffer = new MemoryStream();

        var chunk = new byte[BufferSize];
        int read;
        while ((read = await content.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            sha.AppendData(chunk, 0, read);
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return (buffer.ToArray(), Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant());
    }

    /// <summary>Detects a PostgreSQL primary-key violation from a save failure (SQLSTATE 23505).</summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        const string postgresUniqueViolation = "23505";

        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException postgres && postgres.SqlState == postgresUniqueViolation)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A non-seekable, read-only stream over a BYTEA column that owns the database resources which produced
    /// it: reads flow through the Npgsql reader's sequential stream, and the reader/command/context are
    /// released when the stream is disposed (sync or async) — never before.
    /// </summary>
    private sealed class NpgsqlByteaStream : Stream
    {
        private readonly Stream _inner;
        private readonly NpgsqlDataReader _reader;
        private readonly NpgsqlCommand _command;
        private readonly RelayDbContext _db;

        public NpgsqlByteaStream(Stream inner, NpgsqlDataReader reader, NpgsqlCommand command, RelayDbContext db)
        {
            _inner = inner;
            _reader = reader;
            _command = command;
            _db = db;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _inner.Read(buffer);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => await _inner.ReadAsync(buffer, ct).ConfigureAwait(false);

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => await _inner.ReadAsync(buffer.AsMemory(offset, count), ct).ConfigureAwait(false);

        public override void Flush()
        {
            // Read-only stream; nothing to flush.
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _reader.Dispose();
                _command.Dispose();
                _db.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            await _reader.DisposeAsync().ConfigureAwait(false);
            await _command.DisposeAsync().ConfigureAwait(false);
            await _db.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }
}
