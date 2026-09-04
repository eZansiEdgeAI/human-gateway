using HumanGateway.Edge.Api;
using HumanGateway.Edge.Security;
using HumanGateway.Protocol.Models;
using HumanGateway.Security;

namespace HumanGateway.Edge.Endpoints;

/// <summary>
/// Maps the Edge local user-authentication endpoints (AUTH-FR-02, SP-03): username + password login issuing
/// signed opaque session tokens, logout, the authenticated identity, and local account provisioning. Login
/// failures surface the stable <c>AUTH_REJECTED</c>/<c>FORBIDDEN</c> codes via <see cref="LocalApiException"/>
/// (the global exception handler in Program.cs maps them to <see cref="ProtocolError"/> responses); the token
/// itself is never logged (SP-07). Authorisation middleware (per-conversation/task/artifact, AUTH-FR-03) builds
/// on <see cref="BearerTokens"/> in a later task.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>Maps the local auth endpoint group onto the app.</summary>
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth");

        // POST /auth/login — username + password → signed opaque session token (AUTH-FR-02, SP-03).
        group.MapPost("/login", static async (LoginRequest request, LocalAuthService auth, CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(request.Username, request.Password, ct);
            return Results.Ok(new LoginResponse
            {
                Token = result.Session.Token,
                ExpiresAt = result.Session.ExpiresAt,
                User = result.User,
            });
        });

        // POST /auth/logout — revokes the presented session token (idempotent; unknown tokens are not an error).
        group.MapPost("/logout", static async (HttpRequest http, LocalAuthService auth, CancellationToken ct) =>
        {
            await auth.RevokeSessionAsync(BearerTokens.FromRequest(http) ?? string.Empty, ct);
            return Results.NoContent();
        });

        // GET /auth/me — resolves the presented session token to the authenticated user (AUTH-FR-02, SP-03).
        group.MapGet("/me", static async (HttpRequest http, LocalAuthService auth, CancellationToken ct) =>
        {
            var token = BearerTokens.FromRequest(http);
            if (!BearerTokens.IsWellFormed(token))
            {
                return Unauthorized("A valid session token is required.");
            }

            var user = await auth.AuthenticateAsync(token!, ct);
            return user is null
                ? Unauthorized("The session token is invalid or has expired.")
                : Results.Ok(user);
        });

        // Local account provisioning (AUTH-FR-02). The first account is usually the bootstrap user seeded
        // from configuration at startup (SP-07); the API covers the in-field case.
        group.MapGet("/users", static async (HttpContext context, LocalAuthService auth, CancellationToken ct) =>
        {
            CurrentUser.RequireAdministrator(context);
            return Results.Ok(await auth.ListUsersAsync(ct));
        });

        group.MapPost("/users", static async (HttpContext context, CreateUserRequest request, LocalAuthService auth, CancellationToken ct) =>
        {
            CurrentUser.RequireAdministrator(context);
            var user = await auth.CreateUserAsync(request.Username, request.DisplayName, request.Password, ct);
            return Results.Created($"/auth/users/{user.Id}", user);
        });

        group.MapGet("/users/{id}", static async (HttpContext context, string id, LocalAuthService auth, CancellationToken ct) =>
        {
            CurrentUser.RequireAdministrator(context);
            var user = await auth.GetUserByIdAsync(id, ct);
            return user is null ? ApiErrors.NotFound($"User {id} not found.") : Results.Ok(user);
        });
    }

    private static IResult Unauthorized(string message) => Results.Json(new ProtocolError
    {
        Code = ErrorCodes.Unauthorized,
        Message = message,
        Retryable = false,
    }, statusCode: StatusCodes.Status401Unauthorized);
}
