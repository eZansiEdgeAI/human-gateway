namespace HumanGateway.Relay.Storage.Entities;

/// <summary>
/// Durable remote user-session row at the Relay (AUTH-FR-02, SP-03, external-web-access). Stores only the
/// SHA-256 fingerprint of the issued session token (SP-07 — the plaintext token is returned to the client
/// exactly once and never persisted), together with its expiry and revocation state. One row per issued
/// token.
/// </summary>
public sealed class SessionRecord
{
    /// <summary><c>sha256:&lt;hex&gt;</c> fingerprint of the session token — the primary key (SP-07).</summary>
    public string TokenFingerprint { get; set; } = null!;

    /// <summary>The remote user this session authenticates.</summary>
    public string UserId { get; set; } = null!;

    /// <summary>RFC 3339 UTC instant when the session was issued.</summary>
    public string IssuedAt { get; set; } = null!;

    /// <summary>RFC 3339 UTC instant when the session expires; expired sessions are rejected.</summary>
    public string ExpiresAt { get; set; } = null!;

    /// <summary>RFC 3339 UTC instant when the session was revoked (logout); null while active.</summary>
    public string? RevokedAt { get; set; }
}
