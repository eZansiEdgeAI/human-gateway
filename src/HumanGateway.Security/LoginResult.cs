namespace HumanGateway.Security;

/// <summary>Outcome of a successful username + password login: the issued session plus the public user view.</summary>
public sealed record LoginResult
{
    /// <summary>The freshly issued session token (returned to the client exactly once, SP-07) and its expiry.</summary>
    public required IssuedSession Session { get; init; }

    /// <summary>The authenticated user's public view (no verifier, SP-07).</summary>
    public required UserView User { get; init; }
}
