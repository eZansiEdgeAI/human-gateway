using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HumanGateway.Security;

/// <summary>
/// Request-signing primitives for Edge↔Relay traffic (AUTH-FR-04, SP-01): every outbound request from an Edge
/// Gateway to the Cloud Relay carries an HMAC-SHA256 signature over a canonical representation of the request,
/// keyed with a per-gateway request-signing key derived from the gateway's registration token. The Relay
/// recomputes the same canonical form from the incoming request and verifies it in constant time, so a request
/// cannot be forged, replayed beyond the timestamp skew window, or rebound to a different gateway or endpoint
/// without holding the gateway's secret.
///
/// <para><b>Design notes (identity-security Open Q #1: signed opaque tokens v1, no JWT).</b> The request-signing
/// key is <c>HMAC-SHA256(key = "humangateway:request-signing:v1", message = registrationToken)</c> — a
/// purpose-separated derivation of the registration token. The Edge derives it from its plaintext token; the
/// Relay derives it at token-issue/rotate time and stores the derived key (not the token, SP-07). This keeps the
/// Relay's "fingerprint only" posture: it never holds the registration token, and the derived key is useless for
/// the registration handshake itself.</para>
///
/// <para><b>Canonical form.</b> The signature covers the request method, path, raw query string, an RFC 3339 UTC
/// timestamp, a random nonce, and the gateway id — everything that identifies the endpoint and the caller, plus
/// freshness and replay binding. The body is <em>not</em> hashed: transport integrity is provided by TLS (SP-01)
/// and artifact integrity by content-hash verification (SP-06), while keeping the Relay middleware streaming-safe
/// for large artifact chunks.</para>
/// </summary>
public static class GatewayRequestSigning
{
    /// <summary>Signature scheme version prefix on the wire: <c>v1=&lt;hex&gt;</c>.</summary>
    public const string SchemePrefix = "v1=";

    /// <summary>Header carrying the gateway identity the signature is bound to.</summary>
    public const string GatewayIdHeader = "X-Hg-Gateway-Id";

    /// <summary>Header carrying the RFC 3339 UTC request timestamp.</summary>
    public const string TimestampHeader = "X-Hg-Timestamp";

    /// <summary>Header carrying the per-request random nonce (replay binding).</summary>
    public const string NonceHeader = "X-Hg-Nonce";

    /// <summary>Header carrying the request signature (<c>v1=&lt;hex&gt;</c>).</summary>
    public const string SignatureHeader = "X-Hg-Signature";

    /// <summary>Label that purpose-separates the request-signing key from the registration token.</summary>
    private static readonly byte[] KeyDerivationLabel = Encoding.UTF8.GetBytes("humangateway:request-signing:v1");

    /// <summary>RFC 3339 format with millisecond precision, matching the protocol timestamp style.</summary>
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>
    /// Derives the per-gateway request-signing key from the registration token:
    /// <c>HMAC-SHA256(key = "humangateway:request-signing:v1", message = token)</c>, base64url-encoded. The Edge
    /// and the Relay compute the identical value from the same token, so the Relay only ever stores the derived
    /// key (set when the token is issued or rotated) — never the token itself (SP-07).
    /// </summary>
    public static string DeriveKey(string registrationToken)
    {
        ArgumentNullException.ThrowIfNull(registrationToken);
        var derived = HMACSHA256.HashData(KeyDerivationLabel, Encoding.UTF8.GetBytes(registrationToken));
        return Convert.ToBase64String(derived)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Builds the canonical request string the signature covers: method, path, raw query (with leading '?', or
    /// empty), timestamp, nonce, and gateway id — each on its own line. Both peers construct it from the exact
    /// wire values so any byte difference (including a query-string reorder) invalidates the signature.
    /// </summary>
    public static string Canonicalize(
        string method, string path, string query, string timestamp, string nonce, string gatewayId)
        => string.Join('\n', method, path, query, timestamp, nonce, gatewayId);

    /// <summary>Signs a canonical request string: <c>v1=&lt;lowercase hex HMAC-SHA256&gt;</c>.</summary>
    public static string Sign(string signingKey, string canonical)
    {
        ArgumentNullException.ThrowIfNull(signingKey);
        ArgumentNullException.ThrowIfNull(canonical);
        var key = Convert.FromBase64String(Base64UrlPad(signingKey));
        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonical));
        return SchemePrefix + Convert.ToHexString(mac).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies a presented signature against the expected value in constant time. Accepts the
    /// <c>v1=&lt;hex&gt;</c> wire form only; anything else (including an empty or non-v1 value) is invalid.
    /// </summary>
    public static bool Verify(string signingKey, string canonical, string? presentedSignature)
    {
        if (presentedSignature is null || !presentedSignature.StartsWith(SchemePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var expected = Sign(signingKey, canonical);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var presentedBytes = Encoding.UTF8.GetBytes(presentedSignature);
        return expectedBytes.Length == presentedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, presentedBytes);
    }

    /// <summary>
    /// True when <paramref name="timestamp"/> parses as an RFC 3339 UTC instant within
    /// <paramref name="maxSkew"/> of <paramref name="now"/>. Bounds the replay window: a captured signed request
    /// cannot be replayed indefinitely, and an unsigned re-sign with a shifted clock is rejected.
    /// </summary>
    public static bool IsFresh(string timestamp, DateTimeOffset now, TimeSpan maxSkew)
    {
        if (!DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return false;
        }

        var skew = now - parsed;
        return skew >= -maxSkew && skew <= maxSkew;
    }

    /// <summary>Formats a UTC instant as an RFC 3339 timestamp (same style as the protocol's timestamps).</summary>
    public static string FormatTimestamp(DateTimeOffset timestamp)
        => timestamp.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    /// <summary>Generates a fresh random nonce (16 bytes of entropy, base64url) for replay binding.</summary>
    public static string GenerateNonce()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Signs an outbound <see cref="HttpRequestMessage"/> in place: computes the canonical form from the actual
    /// request (method, decoded absolute path, raw query), a fresh timestamp and nonce, and the gateway id, then
    /// sets the <c>X-Hg-*</c> headers. The path is unescaped to match the Relay's <c>HttpRequest.Path</c>
    /// (ASP.NET serves the decoded path), while the query stays raw — exactly the wire values the middleware
    /// recomputes. The body is intentionally not hashed (transport integrity comes from TLS, SP-01). Used by the
    /// Edge's <c>SignedGatewayRequestHandler</c> and by tests driving the real Relay.
    /// </summary>
    public static void SignRequest(HttpRequestMessage request, string gatewayId, string signingKey, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(gatewayId);
        ArgumentNullException.ThrowIfNull(signingKey);
        ArgumentNullException.ThrowIfNull(time);

        var uri = request.RequestUri ?? throw new InvalidOperationException("Request has no URI to sign.");
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        var timestamp = FormatTimestamp(time.GetUtcNow());
        var nonce = GenerateNonce();
        var canonical = Canonicalize(request.Method.Method, path, uri.Query, timestamp, nonce, gatewayId);
        var signature = Sign(signingKey, canonical);

        request.Headers.TryAddWithoutValidation(GatewayIdHeader, gatewayId);
        request.Headers.TryAddWithoutValidation(TimestampHeader, timestamp);
        request.Headers.TryAddWithoutValidation(NonceHeader, nonce);
        request.Headers.TryAddWithoutValidation(SignatureHeader, signature);
    }

    /// <summary>Reconstitutes padded base64 from an unpadded base64url string (both sides use the same key).</summary>
    private static string Base64UrlPad(string unpadded)
    {
        var normalized = unpadded.Replace('-', '+').Replace('_', '/');
        return (normalized.Length % 4) switch
        {
            2 => normalized + "==",
            3 => normalized + "=",
            _ => normalized,
        };
    }
}
