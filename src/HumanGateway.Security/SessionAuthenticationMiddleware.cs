using HumanGateway.Protocol.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HumanGateway.Security;

/// <summary>
/// ASP.NET middleware that authenticates requests carrying an <c>Authorization: Bearer &lt;token&gt;</c>
/// session token (AUTH-FR-02, SP-03). When a well-formed bearer token is present and valid, the resolved
/// <see cref="AuthenticatedUser"/> is exposed via <see cref="HttpContext.Items"/> under
/// <see cref="CurrentUserKey"/>. Requests without a token pass through unauthenticated — enforcing a
/// required session is the caller's choice (the authorisation middleware, AUTH-FR-03). An invalid or
/// expired token is rejected with 401 <see cref="ErrorCodes.SessionTokenInvalid"/> so a broken client
/// cannot silently fall back to anonymous.
/// </summary>
public sealed class SessionAuthenticationMiddleware
{
    /// <summary><see cref="HttpContext.Items"/> key holding the resolved <see cref="AuthenticatedUser"/>.</summary>
    public const string CurrentUserKey = "HumanGateway.Security.AuthenticatedUser";

    private readonly RequestDelegate _next;
    private readonly IUserSessionService _sessions;

    /// <summary>Creates the middleware over the next delegate and the shared session service.</summary>
    public SessionAuthenticationMiddleware(RequestDelegate next, IUserSessionService sessions)
    {
        _next = next;
        _sessions = sessions;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context)
    {
        var token = BearerTokens.FromRequest(context.Request);
        if (token is not null)
        {
            var user = await _sessions.AuthenticateAsync(token, context.RequestAborted).ConfigureAwait(false);
            if (user is null)
            {
                await RejectAsync(context).ConfigureAwait(false);
                return;
            }

            context.Items[CurrentUserKey] = user;
        }

        await _next(context).ConfigureAwait(false);
    }

    private static Task RejectAsync(HttpContext context)
    {
        var error = new ProtocolError
        {
            Code = ErrorCodes.SessionTokenInvalid,
            Message = "The session token is invalid or has expired.",
            Retryable = false,
        };
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return context.Response.WriteAsJsonAsync(error, context.RequestAborted);
    }
}

/// <summary>Convenience extension for wiring <see cref="SessionAuthenticationMiddleware"/>.</summary>
public static class SessionAuthenticationExtensions
{
    /// <summary>Adds bearer-session authentication to the pipeline (AUTH-FR-02, SP-03).</summary>
    public static IApplicationBuilder UseSessionAuthentication(this IApplicationBuilder app)
        => app.UseMiddleware<SessionAuthenticationMiddleware>();
}
