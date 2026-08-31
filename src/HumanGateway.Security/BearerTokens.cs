using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;

namespace HumanGateway.Security;

/// <summary>
/// Extracts the session bearer token from an incoming request (AUTH-FR-02, SP-03). Both the Edge local API
/// and the Relay remote API authenticate users by the <c>Authorization: Bearer &lt;hgsu_…&gt;</c> header;
/// this shared helper keeps the extraction and shape-check in one place. It never logs the token (SP-07).
/// </summary>
public static class BearerTokens
{
    /// <summary>Scheme required by the Authorization header.</summary>
    public const string Scheme = "Bearer";

    /// <summary>
    /// Returns the presented bearer token when the header is present, uses the <see cref="Scheme"/> scheme
    /// (case-insensitive), and the value is non-empty. Otherwise null.
    /// </summary>
    public static string? FromRequest(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var header = request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        var parts = header.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !string.Equals(parts[0], Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = parts[1].Trim();
        return token.Length is 0 ? null : token;
    }

    /// <summary>True when <paramref name="token"/> is a well-formed <c>hgsu_</c> session token.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Regex source-generated; no trimming impact.")]
    public static bool IsWellFormed(string? token) => SessionTokens.IsWellFormed(token);
}
