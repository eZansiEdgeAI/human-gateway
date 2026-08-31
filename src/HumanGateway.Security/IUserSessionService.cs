namespace HumanGateway.Security;

/// <summary>
/// Shared contract for user session services at the Edge and Relay (AUTH-FR-02, SP-03). Both services issue
/// the same signed opaque session tokens and validate them the same way; only the durable store behind the
/// interface differs (SQLite at the Edge, PostgreSQL at the Relay).
/// </summary>
public interface IUserSessionService
{
    /// <summary>
    /// Issues a fresh session token for <paramref name="userId"/>: generates the opaque token, persists a
    /// session row holding only its fingerprint + expiry, and returns the token + expiry to the caller.
    /// </summary>
    Task<IssuedSession> IssueSessionAsync(string userId, CancellationToken ct);

    /// <summary>
    /// Validates a presented session token: shape check, fingerprint lookup, expiry check, and an ACTIVE
    /// user status check. Returns the authenticated user, or null when the token is unknown/expired/revoked
    /// or the user is no longer active.
    /// </summary>
    Task<AuthenticatedUser?> AuthenticateAsync(string token, CancellationToken ct);

    /// <summary>Revokes a session token (logout). Idempotent: an unknown token is not an error.</summary>
    Task RevokeSessionAsync(string token, CancellationToken ct);
}

/// <summary>A freshly issued session token and its RFC 3339 expiry.</summary>
public sealed record IssuedSession
{
    /// <summary>The opaque session token handed to the client (returned exactly once).</summary>
    public required string Token { get; init; }

    /// <summary>RFC 3339 UTC instant when the token expires.</summary>
    public required string ExpiresAt { get; init; }
}
