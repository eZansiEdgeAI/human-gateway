using HumanGateway.Protocol.Models;

namespace HumanGateway.Relay.Storage.Entities;

/// <summary>
/// Durable Relay storage of an <see cref="Artifact"/> metadata record (RELAY-FR-01). Only the metadata is
/// stored here; the bytes live in the content-addressed BYTEA table <see cref="ArtifactBlobRecord"/>
/// (PROTO-FR-04). The hash column is indexed for deduplication lookups (multiple artifact IDs may reference
/// the same bytes).
/// </summary>
public sealed class ArtifactRecord
{
    /// <summary>Durable artifact ID — the primary key (the storage key).</summary>
    public string Id { get; set; } = null!;

    /// <summary>Content hash (<c>sha256:&lt;hex&gt;</c>) used for dedup and tamper detection (SP-06).</summary>
    public string Hash { get; set; } = null!;

    /// <summary>Byte size for progress reporting and quota enforcement.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Media type for rendering/interpretation.</summary>
    public string MimeType { get; set; } = null!;

    /// <summary>The full protocol artifact record, stored as canonical wire JSON.</summary>
    public Artifact Envelope { get; set; } = null!;

    /// <summary>Creates a storage record from a protocol artifact, deriving the query columns.</summary>
    public static ArtifactRecord FromEnvelope(Artifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        return new ArtifactRecord
        {
            Id = artifact.Id,
            Hash = artifact.Hash,
            SizeBytes = artifact.SizeBytes,
            MimeType = artifact.MimeType,
            Envelope = artifact,
        };
    }
}
