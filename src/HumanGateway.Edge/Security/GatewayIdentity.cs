namespace HumanGateway.Edge.Security;

/// <summary>
/// The Edge Gateway's durable identity record (AUTH-FR-01, gateway.schema.json). This is what the Edge
/// persists locally so it can re-authenticate to the Relay across restarts and long disconnects without
/// re-running the registration handshake.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="RegistrationToken"/> is the gateway's secret: it is the only credential that proves this
/// Edge owns the gateway identity to the Relay. Per SP-07 it must be stored only in the Edge secret store
/// (file with owner-only permissions via <see cref="IGatewaySecretStore"/>), never in code, config committed
/// to a repo, logs, or error payloads.
/// </para>
/// <para>
/// The registration token is intentionally returned as plaintext only during the registration handshake
/// (request/confirm/rotate). Once the Relay has confirmed registration, the Relay keeps only a fingerprint
/// of the token; the Edge keeps the plaintext in its secret store so it can rotate or re-confirm later.
/// </para>
/// </remarks>
public sealed record GatewayIdentity
{
    /// <summary>Unique durable gateway ID (AUTH-FR-01, common.schema.json#/$defs/id).</summary>
    public string GatewayId { get; init; } = null!;

    /// <summary>Current registration lifecycle state from the Edge's perspective.</summary>
    public GatewayIdentityState State { get; init; }

    /// <summary>The plaintext registration token — the gateway's secret (SP-07). Null when unregistered.</summary>
    public string? RegistrationToken { get; init; }

    /// <summary>RFC 3339 UTC expiry of the current registration token, or null when unregistered.</summary>
    public string? TokenExpiresAt { get; init; }

    /// <summary>UTC instant the Relay confirmed registration, or null when not registered.</summary>
    public DateTimeOffset? RegisteredAtUtc { get; init; }

    /// <summary>True when the identity is fully registered and may exchange sync batches (SP-02).</summary>
    public bool IsRegistered => State == GatewayIdentityState.Registered && !string.IsNullOrEmpty(RegistrationToken);
}
