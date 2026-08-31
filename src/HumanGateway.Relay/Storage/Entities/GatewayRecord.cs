namespace HumanGateway.Relay.Storage.Entities;

/// <summary>
/// Durable Relay record of an Edge Gateway identity (gateway.schema.json, AUTH-FR-01, RELAY-FR-03). The Relay
/// stores only the SHA-256 fingerprint of the registration token — never the plaintext token (SP-07) — along
/// with the registration lifecycle status. Only REGISTERED gateways may exchange sync batches; SUSPENDED and
/// REVOKED gateways are rejected (SP-02). One row per gateway.
/// </summary>
public sealed class GatewayRecord
{
    /// <summary>Unique durable gateway identity — the primary key (SP-02). Referenced by syncbatch.gatewayId.</summary>
    public string GatewayId { get; set; } = null!;

    /// <summary>Human-readable gateway name (e.g. the school or site name).</summary>
    public string? DisplayName { get; set; }

    /// <summary>Wire-token registration lifecycle (UNREGISTERED | PENDING | REGISTERED | SUSPENDED | REVOKED).</summary>
    public string? Status { get; set; }

    /// <summary>SHA-256 of the current registration token (<c>sha256:&lt;hex&gt;</c>); never the plaintext (SP-07).</summary>
    public string? RegistrationTokenFingerprint { get; set; }

    /// <summary>RFC 3339 UTC when the current registration token was issued.</summary>
    public string? TokenIssuedAt { get; set; }

    /// <summary>RFC 3339 UTC when the current token expires; the Edge must rotate before this time.</summary>
    public string? TokenExpiresAt { get; set; }

    /// <summary>RFC 3339 UTC when the gateway first registered successfully.</summary>
    public string? RegisteredAt { get; set; }

    /// <summary>RFC 3339 UTC when the gateway was suspended (required when status is SUSPENDED).</summary>
    public string? SuspendedAt { get; set; }

    /// <summary>RFC 3339 UTC when the gateway identity was revoked (required when status is REVOKED).</summary>
    public string? RevokedAt { get; set; }

    /// <summary>RFC 3339 UTC of the last successful authenticated Edge↔Relay exchange.</summary>
    public string? LastSeenAt { get; set; }

    /// <summary>RFC 3339 UTC when the identity record was created.</summary>
    public string CreatedAt { get; set; } = null!;

    /// <summary>RFC 3339 UTC when the identity record last changed.</summary>
    public string? UpdatedAt { get; set; }
}
