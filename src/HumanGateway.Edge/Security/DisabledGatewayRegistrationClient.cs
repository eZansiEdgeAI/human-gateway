using HumanGateway.Protocol.Models;

namespace HumanGateway.Edge.Security;

/// <summary>
/// No-op <see cref="IGatewayRegistrationClient"/> used when no Relay is configured (SP-01, NF-01). The Edge
/// stays LAN-only: it never attempts the registration handshake, and the identity manager reports the gateway
/// as unregistered. Mirrors the <c>DisabledRelaySyncClient</c> pattern from the synchronisation feature.
/// </summary>
public sealed class DisabledGatewayRegistrationClient : IGatewayRegistrationClient
{
    /// <inheritdoc />
    public bool IsConfigured => false;

    /// <inheritdoc />
    /// <remarks>Never called — the identity manager guards on <see cref="IsConfigured"/> first.</remarks>
    public Task<RegistrationTokenIssued> RequestRegistrationAsync(
        string gatewayId, string? displayName, CancellationToken ct)
        => throw new InvalidOperationException("No Relay is configured; RequestRegistrationAsync must not be called.");

    /// <inheritdoc />
    /// <remarks>Never called — the identity manager guards on <see cref="IsConfigured"/> first.</remarks>
    public Task<Gateway> ConfirmRegistrationAsync(string gatewayId, string registrationToken, CancellationToken ct)
        => throw new InvalidOperationException("No Relay is configured; ConfirmRegistrationAsync must not be called.");

    /// <inheritdoc />
    /// <remarks>Never called — the identity manager guards on <see cref="IsConfigured"/> first.</remarks>
    public Task<RegistrationTokenIssued> RotateTokenAsync(string gatewayId, string currentToken, CancellationToken ct)
        => throw new InvalidOperationException("No Relay is configured; RotateTokenAsync must not be called.");
}
