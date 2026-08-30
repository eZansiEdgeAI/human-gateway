using HumanGateway.Core.Hashing;

namespace HumanGateway.Core.Artifacts;

/// <summary>
/// Validation and normalisation of content-hash strings: <c>sha256:&lt;64 lowercase hex&gt;</c>
/// (ARTF-FR-01, schemas/common.schema.json#/$defs/contentHash). A well-formed hash is the algorithm prefix
/// from <see cref="ContentHasher.AlgorithmPrefix"/> followed by exactly <see cref="HexLength"/> hexadecimal
/// characters (case-insensitive on input, normalised to lowercase).
/// </summary>
public static class ArtifactHash
{
    /// <summary>The number of hexadecimal characters in a SHA-256 digest.</summary>
    public const int HexLength = 64;

    /// <summary>
    /// Parses a content-hash string into its lowercase hex digest. Returns <c>false</c> when the string is
    /// null/empty, lacks the <c>sha256:</c> prefix, or is not exactly <see cref="HexLength"/> hex characters.
    /// </summary>
    public static bool TryGetHex(string? hash, out string hex)
    {
        hex = string.Empty;
        // The prefix is the canonical protocol token (schemas/common.schema.json#/$defs/contentHash:
        // "sha256:"); hex digits are accepted case-insensitively and normalised to lowercase below.
        if (string.IsNullOrEmpty(hash) || !hash.StartsWith(ContentHasher.AlgorithmPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = hash[ContentHasher.AlgorithmPrefix.Length..];
        if (candidate.Length != HexLength)
        {
            return false;
        }

        foreach (var c in candidate)
        {
            if (!IsHex(c))
            {
                return false;
            }
        }

        hex = candidate.ToLowerInvariant();
        return true;
    }

    /// <summary>
    /// Returns the lowercase hex digest of a content hash, throwing <see cref="FormatException"/> when it is
    /// malformed. Call this on untrusted input before addressing the store.
    /// </summary>
    public static string RequireHex(string? hash)
        => TryGetHex(hash, out var hex)
            ? hex
            : throw new FormatException($"'{hash}' is not a well-formed content hash (expected sha256:<{HexLength} hex chars>).");

    private static bool IsHex(char c)
        => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
