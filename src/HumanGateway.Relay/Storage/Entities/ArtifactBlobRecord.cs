namespace HumanGateway.Relay.Storage.Entities;

/// <summary>
/// The Relay's content-addressed artifact bytes (RELAY-FR-01): a BYTEA row per unique content hash. Equal
/// content yields an equal hash, so one row serves every artifact ID referencing those bytes (dedup,
/// ARTF-FR-01). Streaming reads are supported by returning <see cref="Data"/> in chunks (task CLOUD-RELAY-4.5).
/// </summary>
public sealed class ArtifactBlobRecord
{
    /// <summary>Content hash (<c>sha256:&lt;hex&gt;</c>) — the primary key (dedup key).</summary>
    public string Hash { get; set; } = null!;

    /// <summary>The artifact bytes.</summary>
    public byte[] Data { get; set; } = null!;

    /// <summary>Byte size of <see cref="Data"/> for quota/progress reporting.</summary>
    public long SizeBytes { get; set; }

    /// <summary>RFC 3339 UTC when the blob was first recorded.</summary>
    public string CreatedAt { get; set; } = null!;
}
