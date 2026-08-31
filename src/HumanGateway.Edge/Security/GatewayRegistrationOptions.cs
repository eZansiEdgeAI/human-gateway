namespace HumanGateway.Edge.Security;

/// <summary>
/// Configuration for the Edge's gateway registration (AUTH-FR-01), bound from the <c>Relay</c> configuration
/// section — the same Relay the artifact transport and (later) the sync transport dial out to. When no Relay
/// base URL is configured the Edge stays offline-first: it keeps its local identity but never attempts the
/// registration handshake (NF-01, SP-01).
/// </summary>
public sealed class GatewayRegistrationOptions
{
    /// <summary>The configuration section this options type binds to (shared with the Relay options).</summary>
    public const string SectionName = "Relay";

    /// <summary>
    /// Base URL of the Relay's HTTP API (e.g. <c>https://relay.example.com</c>). When null/empty the Edge
    /// does not register and stays LAN-only (SP-01).
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>Human-readable gateway name presented to the Relay during registration (e.g. the school name).</summary>
    public string? DisplayName { get; init; }

    /// <summary>True when a Relay base URL is configured, so registration may be attempted.</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>Timeout for each registration-handshake HTTP call (default 15 s).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);
}
