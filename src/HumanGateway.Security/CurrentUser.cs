using Microsoft.AspNetCore.Http;

namespace HumanGateway.Security;

/// <summary>
/// Helpers for endpoint handlers that require an authenticated user (AUTH-FR-02, SP-03). Use after
/// <c>UseSessionAuthentication</c> has run: <see cref="TryGetCurrentUser"/> returns the user resolved from
/// the bearer token, and <see cref="CurrentUser"/> throws when the request is unauthenticated (handlers
/// that only run behind <c>RequireSession</c> call it directly).
/// </summary>
public static class CurrentUser
{
    /// <summary>Resolves the authenticated user for the request, or null when unauthenticated.</summary>
    public static AuthenticatedUser? TryGetCurrentUser(HttpContext context)
        => context.Items.TryGetValue(SessionAuthenticationMiddleware.CurrentUserKey, out var value)
            ? value as AuthenticatedUser
            : null;

    /// <summary>
    /// Returns the authenticated user or throws <see cref="UnauthenticatedRequestException"/>. For endpoints
    /// that must never be reached anonymously.
    /// </summary>
    public static AuthenticatedUser Require(HttpContext context)
        => TryGetCurrentUser(context)
           ?? throw new UnauthenticatedRequestException();

    public static AuthenticatedUser RequireAdministrator(HttpContext context)
    {
        var user = Require(context);
        if (!user.IsAdministrator) throw new ForbiddenRequestException();
        return user;
    }
}

/// <summary>Raised when an endpoint that requires a session is reached without one.</summary>
public sealed class UnauthenticatedRequestException : Exception
{
    /// <summary>Creates the exception.</summary>
    public UnauthenticatedRequestException()
        : base("This endpoint requires an authenticated session.")
    {
    }
}

public sealed class ForbiddenRequestException : Exception
{
    public ForbiddenRequestException()
        : base("Administrator access is required for this endpoint.")
    {
    }
}
