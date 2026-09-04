using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Api;
using HumanGateway.Relay.Services;
using HumanGateway.Security;

namespace HumanGateway.Relay.Endpoints;

/// <summary>
/// Maps the Relay remote user-authentication endpoints (AUTH-FR-02, SP-03, external-web-access): the remote
/// login gate for users outside the school. Username + password login issues signed opaque session tokens;
/// logout revokes; <c>/auth/me</c> resolves the bearer token. Login failures surface the stable
/// <c>AUTH_REJECTED</c>/<c>FORBIDDEN</c> codes via <see cref="GatewayServiceException"/> (the global exception
/// handler in Program.cs maps them to <see cref="ProtocolError"/> responses); the token is never logged (SP-07).
/// </summary>
public static class RemoteAuthEndpoints
{
    /// <summary>Maps the remote auth endpoint group onto the app.</summary>
    public static void MapRemoteAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth");

        // POST /auth/login — username + password → signed opaque session token (AUTH-FR-02, SP-03).
        group.MapPost("/login", static async (LoginRequest request, RemoteAuthService auth, CancellationToken ct) =>
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
        group.MapPost("/logout", static async (HttpRequest http, RemoteAuthService auth, CancellationToken ct) =>
        {
            await auth.RevokeSessionAsync(BearerTokens.FromRequest(http) ?? string.Empty, ct);
            return Results.NoContent();
        });

        // GET /auth/me — resolves the presented session token to the authenticated remote user (AUTH-FR-02, SP-03).
        group.MapGet("/me", static async (HttpRequest http, RemoteAuthService auth, CancellationToken ct) =>
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

        // Remote account provisioning (AUTH-FR-02). The first account is usually the bootstrap user seeded
        // from configuration at startup (SP-07); the API covers the in-field case.
        group.MapGet("/users", static async (HttpContext context, RemoteAuthService auth, CancellationToken ct) =>
        {
            CurrentUser.RequireAdministrator(context);
            return Results.Ok(await auth.ListUsersAsync(ct));
        });

        group.MapPost("/users", static async (HttpContext context, CreateUserRequest request, RemoteAuthService auth, CancellationToken ct) =>
        {
            CurrentUser.RequireAdministrator(context);
            var user = await auth.CreateUserAsync(request.Username, request.DisplayName, request.Password, ct);
            return Results.Created($"/auth/users/{user.Id}", user);
        });

        group.MapGet("/users/{id}", static async (HttpContext context, string id, RemoteAuthService auth, CancellationToken ct) =>
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
