using HumanGateway.Core.Hashing;
using HumanGateway.Core.Time;
using HumanGateway.Protocol.Models;

namespace HumanGateway.Core.Conflict;

/// <summary>How a two-sided merge resolved.</summary>
public enum MergeDisposition
{
    /// <summary>Both copies identical — no change needed.</summary>
    SameContent,

    /// <summary>Per-field last-writer-wins produced a merged envelope.</summary>
    Merged,

    /// <summary>The local copy failed content-hash verification; the remote copy is intact.</summary>
    LocalCorrupt,

    /// <summary>The remote copy failed content-hash verification; the local copy is intact.</summary>
    RemoteCorrupt,

    /// <summary>Not mergeable: both copies fail verification, or they are different messages.</summary>
    Conflict,
}

/// <summary>The result of <see cref="FieldMerge.MergeMessages"/>.</summary>
public sealed record MergeOutcome
{
    /// <summary>The winning/merged envelope, or null when the disposition is <see cref="MergeDisposition.Conflict"/>.</summary>
    public Message? Merged { get; init; }

    /// <summary>How the merge resolved.</summary>
    public MergeDisposition Disposition { get; init; }

    /// <summary>True when a usable envelope was produced.</summary>
    public bool Succeeded => Merged is not null;
}

/// <summary>
/// Conflict resolution when both sync peers mutated the same entity (synchronisation Open Q #1 default):
/// <em>last-writer-wins per field, content-hash-verified</em>. Each candidate is hash-verified first (a corrupt
/// copy never wins); the surviving fields are chosen from the newer write timestamp
/// (<see cref="Message.UpdatedAt"/>, falling back to <see cref="Message.CreatedAt"/>), and the merged envelope's
/// <see cref="Message.ContentHash"/> is recomputed.
/// </summary>
/// <remarks>
/// v1 semantics: a message envelope carries a single <see cref="Message.UpdatedAt"/> for all of its fields, so
/// per-field resolution amounts to choosing each mutable field from the newer envelope. Identity fields
/// (<see cref="Message.Id"/>, <see cref="Message.Sender"/>, <see cref="Message.ConversationId"/>,
/// <see cref="Message.CreatedAt"/>) are immutable and taken from the local copy. Field-level timestamps are a
/// v2 refinement.
/// </remarks>
public static class FieldMerge
{
    /// <summary>Merges two versions of the same message envelope (content-hash-verified, LWW per field).</summary>
    public static MergeOutcome MergeMessages(Message local, Message remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);

        var localValid = ContentHasher.VerifyMessageHash(local);
        var remoteValid = ContentHasher.VerifyMessageHash(remote);

        if (!localValid && !remoteValid)
        {
            return new MergeOutcome { Disposition = MergeDisposition.Conflict };
        }

        if (!localValid)
        {
            return new MergeOutcome { Merged = remote, Disposition = MergeDisposition.LocalCorrupt };
        }

        if (!remoteValid)
        {
            return new MergeOutcome { Merged = local, Disposition = MergeDisposition.RemoteCorrupt };
        }

        // Both hash-verified: they must be the same durable message.
        if (!string.Equals(local.Id, remote.Id, StringComparison.Ordinal))
        {
            return new MergeOutcome { Disposition = MergeDisposition.Conflict };
        }

        if (string.Equals(local.ContentHash, remote.ContentHash, StringComparison.Ordinal))
        {
            return new MergeOutcome { Merged = local, Disposition = MergeDisposition.SameContent };
        }

        var remoteNewer = RemoteIsNewer(local, remote);
        var merged = new Message
        {
            Id = local.Id,
            Sender = local.Sender,
            ConversationId = local.ConversationId,
            CreatedAt = local.CreatedAt,
            Recipients = LastWriterWins(local.Recipients, remote.Recipients, remoteNewer),
            ReplyToMessageId = LastWriterWins(local.ReplyToMessageId, remote.ReplyToMessageId, remoteNewer),
            WorkflowRef = LastWriterWins(local.WorkflowRef, remote.WorkflowRef, remoteNewer),
            HumanTaskId = LastWriterWins(local.HumanTaskId, remote.HumanTaskId, remoteNewer),
            Payload = LastWriterWins(local.Payload, remote.Payload, remoteNewer) ?? local.Payload,
            ArtifactRefs = LastWriterWins(local.ArtifactRefs, remote.ArtifactRefs, remoteNewer),
            CorrelationTokens = LastWriterWins(local.CorrelationTokens, remote.CorrelationTokens, remoteNewer),
            UpdatedAt = LastWriterWins(local.UpdatedAt, remote.UpdatedAt, remoteNewer) ?? local.UpdatedAt,
        };
        merged = merged with { ContentHash = ContentHasher.ComputeMessageHash(merged) };

        return new MergeOutcome { Merged = merged, Disposition = MergeDisposition.Merged };
    }

    /// <summary>
    /// True when <paramref name="remote"/> carries a strictly newer write timestamp than <paramref name="local"/>
    /// (using <see cref="Message.UpdatedAt"/>, falling back to <see cref="Message.CreatedAt"/>). Ties keep local.
    /// </summary>
    public static bool RemoteIsNewer(Message local, Message remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);

        var localAt = EffectiveTimestamp(local);
        var remoteAt = EffectiveTimestamp(remote);
        if (localAt is null)
        {
            return remoteAt is not null;
        }
        return remoteAt is not null && remoteAt > localAt;
    }

    /// <summary>The effective write timestamp for LWW: <see cref="Message.UpdatedAt"/> else <see cref="Message.CreatedAt"/>.</summary>
    public static DateTimeOffset? EffectiveTimestamp(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.UpdatedAt is { } updated && ProtocolTime.TryParse(updated, out var updatedAt))
        {
            return updatedAt;
        }
        if (ProtocolTime.TryParse(message.CreatedAt, out var createdAt))
        {
            return createdAt;
        }
        return null;
    }

    /// <summary>
    /// Per-field last-writer-wins selector: returns the newer writer's value, or the local value when the newer
    /// writer has none (or on a tie). Keeps the field-level structure explicit so per-field timestamps can be
    /// introduced without changing call sites.
    /// </summary>
    public static T? LastWriterWins<T>(T? local, T? remote, bool remoteNewer) where T : class
        => remoteNewer && remote is not null ? remote : local;
}
