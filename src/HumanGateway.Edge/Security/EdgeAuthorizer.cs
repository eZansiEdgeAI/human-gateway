using HumanGateway.Edge.Storage;
using HumanGateway.Protocol.Models;
using HumanGateway.Security;
using Microsoft.EntityFrameworkCore;

namespace HumanGateway.Edge.Security;

/// <summary>
/// Store-backed <see cref="IResourceAuthorizer"/> for the Edge local store (AUTH-FR-03, SP-04): resolves the
/// authenticated user's local participant addresses (<c>participants.user_id</c> → <c>human:</c> addresses) and
/// checks them against conversation membership. Access rules, applied uniformly by the authorisation
/// middleware for single-resource routes and by the service layer for lists/writes:
/// <list type="bullet">
/// <item><b>Conversation</b> — the user is a member (<c>conversation_participants</c>).</item>
/// <item><b>Message</b> — the user can access the message's conversation.</item>
/// <item><b>Task</b> — the user can access the task's conversation <i>or</i> is one of the assigned
/// recipients of the task's request message.</item>
/// <item><b>Artifact</b> — the artifact is referenced (message <c>artifactRefs</c>) in a conversation the
/// user can access; unreferenced artifacts are not accessible to anyone (AUTH-FR-05).</item>
/// </list>
/// No cross-participant access: a user with no linked <c>human:</c> participant can access nothing.
/// </summary>
public sealed class EdgeAuthorizer : IResourceAuthorizer
{
    private readonly IDbContextFactory<EdgeDbContext> _factory;

    /// <summary>Creates the authorizer over the pooled SQLite context factory.</summary>
    public EdgeAuthorizer(IDbContextFactory<EdgeDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc />
    public async Task<bool> CanAccessAsync(AuthenticatedUser user, AuthorizedResource resource, string resourceId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var addresses = await ResolveParticipantAddressesAsync(db, user.UserId, ct).ConfigureAwait(false);
        if (addresses.Count == 0)
        {
            return false;
        }

        return resource switch
        {
            AuthorizedResource.Conversation => await CanAccessConversationAsync(db, addresses, resourceId, ct).ConfigureAwait(false),
            AuthorizedResource.Message => await CanAccessMessageAsync(db, addresses, resourceId, ct).ConfigureAwait(false),
            AuthorizedResource.Task => await CanAccessTaskAsync(db, addresses, resourceId, ct).ConfigureAwait(false),
            AuthorizedResource.Artifact => await CanAccessArtifactAsync(db, addresses, resourceId, ct).ConfigureAwait(false),
            _ => false,
        };
    }

    /// <summary>Resolves the participant addresses linked to a local user account (participant.userId, AUTH-FR-02).</summary>
    internal static async Task<HashSet<string>> ResolveParticipantAddressesAsync(EdgeDbContext db, string userId, CancellationToken ct)
    {
        var addresses = await db.Participants
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.Address)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return addresses.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Conversation ids the user is a member of (any of their participant addresses).</summary>
    internal static async Task<HashSet<string>> AccessibleConversationIdsAsync(EdgeDbContext db, IReadOnlySet<string> addresses, CancellationToken ct)
    {
        if (addresses.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var ids = await db.ConversationParticipants
            .AsNoTracking()
            .Where(cp => addresses.Contains(cp.ParticipantAddress))
            .Select(cp => cp.ConversationId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<bool> CanAccessConversationAsync(EdgeDbContext db, IReadOnlySet<string> addresses, string conversationId, CancellationToken ct)
    {
        if (addresses.Count == 0)
        {
            return false;
        }

        return await db.ConversationParticipants
            .AsNoTracking()
            .AnyAsync(cp => cp.ConversationId == conversationId && addresses.Contains(cp.ParticipantAddress), ct)
            .ConfigureAwait(false);
    }

    private static async Task<bool> CanAccessMessageAsync(EdgeDbContext db, IReadOnlySet<string> addresses, string messageId, CancellationToken ct)
    {
        var conversationId = await db.Messages
            .AsNoTracking()
            .Where(m => m.Id == messageId)
            .Select(m => m.ConversationId)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return conversationId is not null
            && await CanAccessConversationAsync(db, addresses, conversationId, ct).ConfigureAwait(false);
    }

    private static async Task<bool> CanAccessTaskAsync(EdgeDbContext db, IReadOnlySet<string> addresses, string taskId, CancellationToken ct)
    {
        var requestMessageId = await db.Tasks
            .AsNoTracking()
            .Where(t => t.Id == taskId)
            .Select(t => t.RequestMessageId)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (requestMessageId is null)
        {
            return false;
        }

        var message = await db.Messages
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.Id == requestMessageId, ct)
            .ConfigureAwait(false);
        if (message is null)
        {
            return false;
        }

        // Member of the task's conversation, or one of the assigned recipients of the request message.
        if (await CanAccessConversationAsync(db, addresses, message.ConversationId, ct).ConfigureAwait(false))
        {
            return true;
        }

        return message.Envelope.Recipients?.Any(r => r.Address is not null && addresses.Contains(r.Address)) ?? false;
    }

    private static async Task<bool> CanAccessArtifactAsync(EdgeDbContext db, IReadOnlySet<string> addresses, string artifactId, CancellationToken ct)
    {
        var conversationIds = await AccessibleConversationIdsAsync(db, addresses, ct).ConfigureAwait(false);
        if (conversationIds.Count == 0)
        {
            return false;
        }

        var messages = await db.Messages
            .AsNoTracking()
            .Where(m => conversationIds.Contains(m.ConversationId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return messages.Any(m => m.Envelope.ArtifactRefs?.Any(r => r.Id == artifactId) ?? false);
    }
}
