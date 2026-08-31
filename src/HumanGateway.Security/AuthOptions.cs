using System.Text.Json.Serialization;

namespace HumanGateway.Security;

/// <summary>
/// User-authentication settings bound from the <c>Auth</c> configuration section (AUTH-FR-02, SP-07).
/// The session token lifetime and the bootstrap user (created on first boot from env/secret-store supplied
/// credentials — never from the repo, SP-07) are shared by the Edge and Relay.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>Configuration section key.</summary>
    public const string SectionName = "Auth";

    /// <summary>Session token lifetime (default 12 hours).</summary>
    public TimeSpan SessionTtl { get; init; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Bootstrap user seeded at startup so the first local/remote login is possible before any account has
    /// been provisioned through the API. Credentials come from configuration (env/secret store in
    /// deployment); a committed password here is a release-blocker (SP-07).
    /// </summary>
    public BootstrapUserOptions? BootstrapUser { get; init; }
}

/// <summary>Credentials for the first user account, supplied via env/secret store (SP-07).</summary>
public sealed class BootstrapUserOptions
{
    public string? Username { get; init; }

    public string? Password { get; init; }

    public string? DisplayName { get; init; }
}
