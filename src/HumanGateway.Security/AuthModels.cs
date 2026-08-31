namespace HumanGateway.Security;

// -----------------------------------------------------------------------------------------------
// Shared request / response contracts for the user-authentication API (AUTH-FR-02, SP-03). Both the
// Edge local API and the Relay remote API expose the same login/logout/me surface for the PWA, so the
// wire shapes are defined once here. The authenticated-user payload is the shared <see cref="UserView"/>
// record (never carries the verifier — SP-07). The HTTP JSON layer's camelCase policy (LocalApiJson /
// RelayJson) applies; these plain records carry no explicit JsonPropertyName attributes.
// -----------------------------------------------------------------------------------------------

/// <summary>Request body for <c>POST /auth/login</c> (username + password, v1).</summary>
public sealed record LoginRequest
{
    public string Username { get; init; } = null!;
    public string Password { get; init; } = null!;
}

/// <summary>Request body for <c>POST /auth/users</c> (account provisioning).</summary>
public sealed record CreateUserRequest
{
    public string Username { get; init; } = null!;
    public string DisplayName { get; init; } = null!;
    public string Password { get; init; } = null!;
}

/// <summary>Successful login response: the signed opaque session token (returned exactly once, SP-07), its RFC 3339 expiry, and the public user view.</summary>
public sealed record LoginResponse
{
    public string Token { get; init; } = null!;
    public string ExpiresAt { get; init; } = null!;
    public UserView User { get; init; } = null!;
}
