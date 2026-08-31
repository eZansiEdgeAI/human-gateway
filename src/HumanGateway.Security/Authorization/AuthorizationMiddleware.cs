using HumanGateway.Protocol.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HumanGateway.Security;

/// <summary>
/// Authorisation middleware (AUTH-FR-03, SP-04): the per-conversation/task/artifact access-control gate that
/// runs after <see cref="SessionAuthenticationMiddleware"/> in the request pipeline. It recognises the
/// protected local-API route families (conversations, messages, tasks, artifacts, and the gateway sync-status
/// snapshot) and enforces two things:
/// <list type="number">
/// <item><b>Session required</b> — every protected route must carry a valid bearer session, else 401
/// <c>UNAUTHORIZED</c> (a broken client cannot silently fall back to anonymous).</item>
/// <item><b>Resource access</b> — routes naming a single resource
/// (<c>/conversations/{id}</c>, <c>/messages/{id}</c>, <c>/tasks/{id}[/response]</c>, and the artifact
/// <em>download</em> <c>/artifacts/{id}/content</c>) are delegated to the store-backed
/// <see cref="IResourceAuthorizer"/>, which enforces membership / assignment. Denials are 403 with the
/// resource-specific reserved code (<c>CONVERSATION_ACCESS_DENIED</c>, <c>TASK_ACCESS_DENIED</c>,
/// <c>ARTIFACT_ACCESS_DENIED</c>) — never a 404, so a non-existent resource is indistinguishable from an
/// inaccessible one (SP-07). Artifact metadata, uploads, and content status are session-only: the creator's
/// register → upload → attach flow must work before a message references the artifact.</item>
/// </list>
/// List and write routes without a resource id in the path (<c>/conversations</c>, <c>/messages</c>,
/// <c>/tasks</c>, <c>/artifacts</c>) are session-gated here; the service layer filters lists to the user's
/// accessible set and validates the acting participant on writes (no cross-participant access, SP-04).
/// Routes outside the protected table (auth, health probes) pass through untouched.
/// </summary>
public sealed class AuthorizationMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Creates the middleware over the next delegate.</summary>
    public AuthorizationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context)
    {
        var match = Match(context.Request);
        if (match is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Every protected route requires a valid session.
        var user = CurrentUser.TryGetCurrentUser(context);
        if (user is null)
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized,
                "A valid session is required to access this resource.").ConfigureAwait(false);
            return;
        }

        // Session-only routes (lists and writes; the service filters/validates against the user).
        if (match.Resource is null || match.ResourceId is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var authorizer = context.RequestServices.GetRequiredService<IResourceAuthorizer>();
        if (!await authorizer.CanAccessAsync(user, match.Resource.Value, match.ResourceId, context.RequestAborted)
                .ConfigureAwait(false))
        {
            await RejectAsync(context, StatusCodes.Status403Forbidden, match.DeniedCode!,
                $"You do not have access to this {ResourceName(match.Resource.Value)}.").ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }

    /// <summary>Recognised protected route descriptor. A null <see cref="Resource"/>/<see cref="ResourceId"/>
    /// means the route is session-only (the service layer filters or validates the actor).</summary>
    private sealed record RouteMatch(AuthorizedResource? Resource, string? ResourceId, string? DeniedCode)
    {
        public static RouteMatch SessionOnly { get; } = new(null, null, null!);

        public static RouteMatch ForResource(AuthorizedResource resource, string resourceId, string deniedCode)
            => new(resource, resourceId, deniedCode);
    }

    /// <summary>
    /// Maps a request to the protected-route table. Returns null for routes the middleware does not gate.
    /// Segment shapes mirror the Edge local API (LocalApiEndpoints): the middleware is host-agnostic, so the
    /// same table applies to the Relay once user-facing resource endpoints land (external-web-access).
    /// </summary>
    private static RouteMatch? Match(HttpRequest request)
    {
        var path = request.Path.Value;
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        return segments[0] switch
        {
            "conversations" => segments.Length switch
            {
                1 => RouteMatch.SessionOnly,
                2 => RouteMatch.ForResource(AuthorizedResource.Conversation, segments[1], ErrorCodes.ConversationAccessDenied),
                3 when segments[2] == "messages" => RouteMatch.ForResource(AuthorizedResource.Conversation, segments[1], ErrorCodes.ConversationAccessDenied),
                _ => null,
            },
            "messages" => segments.Length switch
            {
                1 => RouteMatch.SessionOnly,
                2 => RouteMatch.ForResource(AuthorizedResource.Message, segments[1], ErrorCodes.ConversationAccessDenied),
                _ => null,
            },
            "tasks" => segments.Length switch
            {
                1 => RouteMatch.SessionOnly,
                2 => RouteMatch.ForResource(AuthorizedResource.Task, segments[1], ErrorCodes.TaskAccessDenied),
                3 when segments[2] == "response" => RouteMatch.ForResource(AuthorizedResource.Task, segments[1], ErrorCodes.TaskAccessDenied),
                _ => null,
            },
            "artifacts" => segments.Length switch
            {
                1 => RouteMatch.SessionOnly,
                2 => RouteMatch.SessionOnly,
                3 when segments[2] == "content" => HttpMethods.IsGet(request.Method)
                    // Download is content access (AUTH-FR-05: downloads authorised per participant/conversation).
                    ? RouteMatch.ForResource(AuthorizedResource.Artifact, segments[1], ErrorCodes.ArtifactAccessDenied)
                    // Upload (PUT) is a creator action in the register → upload → attach flow: session-only.
                    : RouteMatch.SessionOnly,
                4 when segments[2] == "content" && segments[3] == "status" => RouteMatch.SessionOnly,
                _ => null,
            },
            "sync" => segments.Length == 2 && segments[1] == "status" ? RouteMatch.SessionOnly : null,
            _ => null,
        };
    }

    private static string ResourceName(AuthorizedResource resource) => resource switch
    {
        AuthorizedResource.Conversation => "conversation",
        AuthorizedResource.Message => "message",
        AuthorizedResource.Task => "task",
        AuthorizedResource.Artifact => "artifact",
        _ => "resource",
    };

    private static Task RejectAsync(HttpContext context, int statusCode, string code, string message)
    {
        var error = new ProtocolError
        {
            Code = code,
            Message = message,
            Retryable = false,
        };
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(error, context.RequestAborted);
    }
}

/// <summary>Convenience extension for wiring <see cref="AuthorizationMiddleware"/>.</summary>
public static class AuthorizationExtensions
{
    /// <summary>
    /// Adds per-conversation/task/artifact authorisation to the pipeline (AUTH-FR-03, SP-04). Must be added
    /// after <c>UseSessionAuthentication</c> (it reads the resolved <see cref="AuthenticatedUser"/>) and after
    /// an <c>IResourceAuthorizer</c> is registered in DI. Named <c>UseResourceAuthorization</c> to avoid
    /// colliding with ASP.NET Core's <c>UseAuthorization</c>.
    /// </summary>
    public static IApplicationBuilder UseResourceAuthorization(this IApplicationBuilder app)
        => app.UseMiddleware<AuthorizationMiddleware>();
}
