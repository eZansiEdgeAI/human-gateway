using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HumanGateway.Protocol.Models;

namespace HumanGateway.Core.Hashing;

/// <summary>
/// Content hashing (SYNC-FR-02, SP-06): SHA-256 over the canonical encoding, expressed as
/// <c>sha256:&lt;lowercase-hex&gt;</c> (schemas/common.schema.json#/$defs/contentHash). Computed and
/// verified by both sync peers so tamper/corruption is detected.
/// </summary>
public static class ContentHasher
{
    /// <summary>The content-hash algorithm prefix.</summary>
    public const string AlgorithmPrefix = "sha256:";

    /// <summary>Computes the content hash of a byte buffer.</summary>
    public static string Compute(ReadOnlySpan<byte> content)
        => AlgorithmPrefix + Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>Computes the content hash of a stream.</summary>
    public static string Compute(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return AlgorithmPrefix + Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    /// <summary>Computes the content hash of UTF-8 text.</summary>
    public static string ComputeUtf8(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Compute(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Computes a message's canonical content hash: SHA-256 of the canonical JSON encoding of the envelope
    /// <em>excluding</em> <see cref="Message.ContentHash"/> itself (message.schema.json, SYNC-FR-02).
    /// Serialisation uses the shared wire contract (<see cref="ProtocolJson.Options"/>) so both peers hash
    /// byte-identical input.
    /// </summary>
    public static string ComputeMessageHash(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var withoutHash = message with { ContentHash = null! };
        return ComputeUtf8(JsonSerializer.Serialize(withoutHash, ProtocolJson.Options));
    }

    /// <summary>Verifies a message's declared <see cref="Message.ContentHash"/> against its canonical encoding.</summary>
    public static bool VerifyMessageHash(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return string.Equals(message.ContentHash, ComputeMessageHash(message), StringComparison.Ordinal);
    }

    /// <summary>Verifies declared content hash against actual bytes (constant-time string comparison).</summary>
    public static bool Verify(string declaredHash, ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(declaredHash);
        return string.Equals(declaredHash, Compute(content), StringComparison.Ordinal);
    }
}
