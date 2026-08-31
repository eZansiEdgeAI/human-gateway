namespace HumanGateway.Security;

/// <summary>
/// Store-backed authorisation check used by the authorisation middleware (AUTH-FR-03, SP-04). Given the
/// authenticated user and a protected resource (conversation / message / task / artifact), returns whether
/// the user may access it — membership in the resource's conversation, or assignment for tasks. Each host
/// (Edge SQLite, Relay PostgreSQL) implements this over its own store; the middleware stays host-agnostic.
/// A <see langword="false"/> result is a hard denial (403): the middleware never distinguishes "you are not
/// a member" from "this does not exist" so resource existence cannot be probed (SP-07).
/// </summary>
public interface IResourceAuthorizer
{
    /// <summary>Returns true when the user may access the named resource, false otherwise.</summary>
    Task<bool> CanAccessAsync(AuthenticatedUser user, AuthorizedResource resource, string resourceId, CancellationToken ct);
}
