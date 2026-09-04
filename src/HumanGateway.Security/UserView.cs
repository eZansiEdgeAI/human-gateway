namespace HumanGateway.Security;

/// <summary>
/// Public representation of a user account for API responses (AUTH-FR-02, SP-07). Deliberately excludes
/// <c>passwordVerifier</c> — the verifier is a local-store-only field that must never be transmitted in any
/// protocol payload (user.schema.json, SP-07).
/// </summary>
public sealed record UserView
{
    /// <summary>Durable user id (user.schema.json#/id).</summary>
    public required string Id { get; init; }

    /// <summary>Login username (lowercase).</summary>
    public required string Username { get; init; }

    /// <summary>Human-readable display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Wire-token user status (<c>ACTIVE</c> | <c>DISABLED</c>).</summary>
    public required string Status { get; init; }

    public required string Role { get; init; }

    /// <summary>RFC 3339 UTC instant of the last successful login.</summary>
    public string? LastLoginAt { get; init; }

    /// <summary>RFC 3339 UTC instant when the account was disabled (DISABLED only).</summary>
    public string? DisabledAt { get; init; }

    /// <summary>RFC 3339 UTC instant when the account was created.</summary>
    public required string CreatedAt { get; init; }

    /// <summary>RFC 3339 UTC instant of the last update.</summary>
    public string? UpdatedAt { get; init; }
}
