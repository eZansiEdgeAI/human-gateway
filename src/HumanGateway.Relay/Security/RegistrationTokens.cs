using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HumanGateway.Relay.Security;

/// <summary>
/// Registration-token primitives for gateway identity (AUTH-FR-01, SP-02, SP-07). The Relay generates the
/// high-entropy token (<c>hgrt_</c> + 256 bits of base64url randomness, matching gateway.schema.json
/// <c>$defs.registrationToken</c>), returns it to the Edge exactly once over TLS, and stores only its
/// SHA-256 fingerprint (<c>sha256:&lt;hex&gt;</c>) — the plaintext never persists and is never logged (SP-07).
/// Verification is constant-time so a timing side-channel cannot leak token bytes.
/// </summary>
public static partial class RegistrationTokens
{
    /// <summary>Registration-token prefix (gateway.schema.json <c>$defs.registrationToken</c> pattern).</summary>
    public const string TokenPrefix = "hgrt_";

    /// <summary>Random bytes per token — 256 bits of entropy.</summary>
    public const int TokenRandomBytes = 32;

    /// <summary>
    /// The token body after the prefix: 43..251 chars of base64url. Combined with the 5-char prefix this
    /// honours the schema's 48..256 min/maxLength.
    /// </summary>
    private const string TokenBodyPattern = "[A-Za-z0-9_-]{43,251}";

    /// <summary>Full wire shape: <c>hgrt_</c> + base64url body (48..256 chars).</summary>
    [GeneratedRegex($"^{TokenPrefix}{TokenBodyPattern}$")]
    private static partial Regex TokenShapeRegex();

    /// <summary>
    /// Generates a fresh registration token: <c>hgrt_</c> + 32 bytes of cryptographic randomness encoded as
    /// unpadded base64url (43 chars), for a total length of 48 — exactly the schema's minimum.
    /// </summary>
    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[TokenRandomBytes];
        RandomNumberGenerator.Fill(bytes);
        return TokenPrefix + Base64Url(bytes);
    }

    /// <summary>
    /// Computes the storage fingerprint of a token: <c>sha256:&lt;64 lowercase hex&gt;</c>
    /// (gateway.schema.json <c>registrationTokenFingerprint</c> pattern). The fingerprint, never the plaintext,
    /// is what the Relay persists (SP-07).
    /// </summary>
    public static string Fingerprint(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>True when <paramref name="token"/> matches the <c>hgrt_</c> wire shape (48..256 chars).</summary>
    public static bool IsWellFormed(string? token)
        => token is not null && TokenShapeRegex().IsMatch(token);

    /// <summary>
    /// Verifies a presented token against the stored fingerprint using a constant-time comparison of the two
    /// fingerprint strings (SP-07 — mitigates timing side-channels).
    /// </summary>
    public static bool Verify(string? presentedToken, string? storedFingerprint)
    {
        if (presentedToken is null || storedFingerprint is null)
        {
            return false;
        }

        var candidate = Fingerprint(presentedToken);
        var candidateBytes = Encoding.UTF8.GetBytes(candidate);
        var storedBytes = Encoding.UTF8.GetBytes(storedFingerprint);
        return candidateBytes.Length == storedBytes.Length
            && CryptographicOperations.FixedTimeEquals(candidateBytes, storedBytes);
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
