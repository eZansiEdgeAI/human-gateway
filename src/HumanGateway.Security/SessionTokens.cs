using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HumanGateway.Security;

/// <summary>
/// Session-token primitives for authenticated users (AUTH-FR-02, SP-03). A session token is an opaque,
/// high-entropy bearer credential (<c>hgsu_</c> + 256 bits of base64url randomness) issued to a user after
/// a successful login. Following the gateway registration-token pattern (identity-security Open Q #1:
/// signed opaque/session tokens v1; JWT only if a consumer needs it), the service stores only the SHA-256
/// fingerprint of the token in its sessions table — never the plaintext token (SP-07). Verification is a
/// constant-time fingerprint comparison so a timing side-channel cannot leak token bytes.
/// </summary>
public static partial class SessionTokens
{
    /// <summary>Session-token prefix (mirrors the gateway registration token's <c>hgrt_</c> prefix).</summary>
    public const string TokenPrefix = "hgsu_";

    /// <summary>Random bytes per token — 256 bits of entropy.</summary>
    public const int TokenRandomBytes = 32;

    /// <summary>
    /// The token body after the prefix: 43..251 chars of base64url. Combined with the 5-char prefix this
    /// yields a 48..256-char token, matching the gateway schema's requestToken bounds.
    /// </summary>
    private const string TokenBodyPattern = "[A-Za-z0-9_-]{43,251}";

    /// <summary>Full wire shape: <c>hgsu_</c> + base64url body.</summary>
    [GeneratedRegex($"^{TokenPrefix}{TokenBodyPattern}$")]
    private static partial Regex TokenShapeRegex();

    /// <summary>Generates a fresh session token: <c>hgsu_</c> + 32 bytes of cryptographic randomness.</summary>
    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[TokenRandomBytes];
        RandomNumberGenerator.Fill(bytes);
        return TokenPrefix + Base64Url(bytes);
    }

    /// <summary>Computes the storage fingerprint: <c>sha256:&lt;64 lowercase hex&gt;</c> — what a session row stores.</summary>
    public static string Fingerprint(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>True when <paramref name="token"/> matches the <c>hgsu_</c> wire shape.</summary>
    public static bool IsWellFormed(string? token)
        => token is not null && TokenShapeRegex().IsMatch(token);

    /// <summary>
    /// Verifies a presented token against a stored fingerprint using a constant-time comparison of the two
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
