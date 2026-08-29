using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace HumanGateway.Protocol.Models;

/// <summary>
/// A first-class content object (image, PDF, document, audio) stored by the Edge filesystem / Relay
/// PostgreSQL (BYTEA) and referenced by messages by ID + hash — never embedded in the envelope
/// (artifact.schema.json, PROTO-FR-04, ARTF-FR-01).
/// </summary>
public sealed record Artifact
{
    /// <summary>Durable artifact ID; the storage key (content-hash naming means equal content yields equal hash).</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = null!;

    /// <summary>SHA-256 of the artifact bytes (<c>sha256:&lt;hex&gt;</c>); used for dedup and tamper detection (SP-06).</summary>
    [JsonPropertyName("hash")]
    public string Hash { get; init; } = null!;

    /// <summary>Byte size (protocol ceiling 512 MiB; default gateway limit 50 MiB).</summary>
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    /// <summary>Media type so the receiving app can render or interpret the content.</summary>
    [JsonPropertyName("mimeType")]
    public string MimeType { get; init; } = null!;

    /// <summary>Original filename, preserved for the recipient.</summary>
    [JsonPropertyName("filename")]
    public string Filename { get; init; } = null!;

    /// <summary>Optional human-readable description / alt text.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>When the artifact was first recorded.</summary>
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = null!;
}

/// <summary>
/// A reference to an artifact carried inside a message envelope or task response: ID + content hash plus
/// rendering metadata. Never the bytes (PROTO-FR-04).
/// </summary>
public sealed record ArtifactReference
{
    /// <summary>Artifact ID the receiving gateway resolves in its artifact store.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = null!;

    /// <summary>Content hash the receiving gateway verifies after transfer.</summary>
    [JsonPropertyName("hash")]
    public string Hash { get; init; } = null!;

    /// <summary>Original filename, preserved for the recipient.</summary>
    [JsonPropertyName("filename")]
    public string? Filename { get; init; }

    /// <summary>Media type for rendering.</summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }

    /// <summary>Byte size for progress reporting.</summary>
    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; init; }
}
