using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HumanGateway.Security;

/// <summary>
/// PHC-string password hashing for local/remote user credentials (user.schema.json#/passwordVerifier,
/// AUTH-FR-02, SP-07). v1 uses PBKDF2-HMAC-SHA256 with a per-hash random salt, encoded as a PHC string:
/// <c>$pbkdf2-sha256$i=&lt;iterations&gt;,l=&lt;bytes&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;</c>.
/// </summary>
/// <remarks>
/// The PHC format keeps the verifier self-describing (algorithm, cost, salt, digest) so future rounds can
/// raise the cost and re-verify old hashes — no per-site migration needed. Salt and digest are standard
/// base64 (schema pattern <c>^\$[A-Za-z0-9+./=,$-]{19,511}$</c>). The verifier is a local-store-only field:
/// it is hashed with a cost parameter (default <see cref="DefaultIterations"/>, OWASP-recommended for
/// PBKDF2-SHA256) and is never transmitted in any protocol payload and never logged (SP-07).
/// </remarks>
public static partial class PasswordHasher
{
    /// <summary>Default PBKDF2-SHA256 cost (OWASP recommendation).</summary>
    public const int DefaultIterations = 210_000;

    /// <summary>Salt length in bytes (128 bits).</summary>
    private const int SaltBytes = 16;

    /// <summary>Digest length in bytes (256 bits, matching SHA-256).</summary>
    private const int HashBytes = 32;

    /// <summary>Algorithm token used in the PHC string.</summary>
    public const string Algorithm = "pbkdf2-sha256";

    [GeneratedRegex(@"^\$pbkdf2-sha256\$i=([0-9]+),l=([0-9]+)\$([A-Za-z0-9+/=]+)\$([A-Za-z0-9+/=]+)$")]
    private static partial Regex PhcRegex();

    /// <summary>
    /// Hashes <paramref name="password"/> into a PHC string. The salt is drawn from a cryptographic RNG;
    /// identical passwords never produce the same verifier.
    /// </summary>
    public static string Hash(string password, int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (iterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iterations must be positive.");
        }

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var digest = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"${Algorithm}$i={iterations},l={HashBytes}${Convert.ToBase64String(salt)}${Convert.ToBase64String(digest)}";
    }

    /// <summary>
    /// Verifies <paramref name="password"/> against a PHC verifier. The stored algorithm/cost/salt drive the
    /// recomputation, so any previously-issued verifier stays valid; the digest comparison is constant-time.
    /// </summary>
    public static bool Verify(string password, string phcVerifier)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(phcVerifier);

        var match = PhcRegex().Match(phcVerifier);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, out var iterations) || iterations < 1
            || !int.TryParse(match.Groups[2].Value, out var length) || length < 1)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(match.Groups[3].Value);
            expected = Convert.FromBase64String(match.Groups[4].Value);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>True when <paramref name="value"/> is a syntactically-valid PHC verifier (schema pattern).</summary>
    public static bool IsPhcVerifier(string? value)
        => value is not null && PhcRegex().IsMatch(value);
}
