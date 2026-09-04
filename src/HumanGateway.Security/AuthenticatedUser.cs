namespace HumanGateway.Security;

/// <summary>
/// The authenticated user resolved from a valid session token (AUTH-FR-02, SP-03). Carries just enough
/// identity for the service layer to authorise requests: the user id (referenced from
/// participant.userId), the username, and a display name. HumanGateway performs no role-checking here
/// (SP-09) — per-conversation/task/artifact authorisation is enforced by the authorisation middleware
/// against conversation membership (AUTH-FR-03).
/// </summary>
public sealed record AuthenticatedUser
{
    /// <summary>Durable local/remote user id (user.schema.json#/id).</summary>
    public required string UserId { get; init; }

    /// <summary>Login username (lowercase).</summary>
    public required string Username { get; init; }

    /// <summary>Human-readable display name.</summary>
    public required string DisplayName { get; init; }

    public string Role { get; init; } = "USER";

    public bool IsAdministrator => Role == "ADMIN";

    /// <summary>RFC 3339 UTC instant when the session token expires.</summary>
    public required string ExpiresAt { get; init; }
}
