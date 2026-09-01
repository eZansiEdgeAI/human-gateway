namespace HumanGateway.Security;

/// <summary>
/// TLS scheme enforcement for Edge↔Relay traffic (AUTH-FR-04, SP-01): the Edge may only dial out to the Relay
/// over <c>https</c>. Plain <c>http</c> is accepted only for explicitly whitelisted development scenarios
/// (loopback hosts, or an explicit insecure-dev opt-in) so the LAN-only PoC and the local dev compose keep
/// working without weakening the production posture.
/// </summary>
public static class RelayTlsPolicy
{
    /// <summary>
    /// Validates a Relay base URL's scheme. Returns the parsed absolute URI when the scheme is permitted,
    /// otherwise throws <see cref="ArgumentException"/> (configuration error — fail fast at startup, SP-01).
    /// </summary>
    /// <param name="baseUrl">The configured Relay base URL.</param>
    /// <param name="allowInsecureHttp">
    /// When true, plain <c>http://</c> is accepted for any host (dev/test only). When false (the default),
    /// only <c>https://</c> and loopback <c>http://</c> hosts are accepted.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The base URL is missing, unparsable, or uses a scheme that is not permitted by SP-01.
    /// </exception>
    public static Uri RequireAllowed(string? baseUrl, bool allowInsecureHttp = false)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("The Relay base URL is required for outbound Edge↔Relay traffic (SP-01).",
                nameof(baseUrl));
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"The Relay base URL '{baseUrl}' is not a valid absolute URI (SP-01).",
                nameof(baseUrl));
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            if (allowInsecureHttp || IsLoopback(uri))
            {
                return uri;
            }

            throw new ArgumentException(
                "All Edge↔Relay traffic must be encrypted in transit (SP-01): the Relay base URL must use "
                + $"https:// (got '{baseUrl}'). For local development only, set Relay__AllowInsecureHttp=true "
                + "or point at a loopback host.",
                nameof(baseUrl));
        }

        throw new ArgumentException(
            $"The Relay base URL '{baseUrl}' uses unsupported scheme '{uri.Scheme}' (https required, SP-01).",
            nameof(baseUrl));
    }

    private static bool IsLoopback(Uri uri)
        => uri.IsLoopback
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(uri.Host, "::1", StringComparison.Ordinal);
}
