using HumanGateway.Protocol.Models;

namespace HumanGateway.Edge.Security;

/// <summary>
/// The wire response of <c>POST /gateways</c> and <c>POST /gateways/{gatewayId}/rotate</c> (camelCase JSON,
/// mirroring the Relay's <c>RegistrationIssued</c>). The <see cref="RegistrationToken"/> is returned exactly
/// once over TLS; the Edge stores it in its secret store (SP-07).
/// </summary>
public sealed record RegistrationTokenIssued
{
    public string GatewayId { get; init; } = null!;

    /// <summary>Gateway registration lifecycle after this call (PENDING, or REGISTERED after a rotation).</summary>
    public string Status { get; init; } = null!;

    /// <summary>The one-time plaintext registration token. Handle as a secret.</summary>
    public string RegistrationToken { get; init; } = null!;

    public string TokenIssuedAt { get; init; } = null!;

    public string TokenExpiresAt { get; init; } = null!;
}

/// <summary>
/// Port for the Edge's outbound registration client (AUTH-FR-01, SP-02): the two-step handshake against the
/// Relay — request a registration token, then present it to confirm registration — plus token rotation.
/// Implementations must never log the token or include it in exception messages (SP-07).
/// </summary>
public interface IGatewayRegistrationClient
{
    /// <summary>True when a Relay is configured so registration may be attempted (SP-01).</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Step 1 — <c>POST /gateways</c>: requests a registration token for this gateway. The Relay creates the
    /// identity in PENDING and returns the one-time token.
    /// </summary>
    Task<RegistrationTokenIssued> RequestRegistrationAsync(string gatewayId, string? displayName, CancellationToken ct);

    /// <summary>
    /// Step 2 — <c>POST /gateways/{gatewayId}/register</c>: presents the token to complete registration.
    /// Returns the canonical <see cref="Gateway"/> record with status REGISTERED.
    /// </summary>
    Task<Gateway> ConfirmRegistrationAsync(string gatewayId, string registrationToken, CancellationToken ct);

    /// <summary>
    /// <c>POST /gateways/{gatewayId}/rotate</c>: rotates a registered gateway's token. The current token is
    /// verified by the Relay, then a fresh one is returned.
    /// </summary>
    Task<RegistrationTokenIssued> RotateTokenAsync(string gatewayId, string currentToken, CancellationToken ct);
}
