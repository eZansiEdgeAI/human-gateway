namespace HumanGateway.Edge.Storage.Entities;

/// <summary>
/// A local conversation — the grouping record that <see cref="MessageRecord"/> envelopes reference by
/// <see cref="MessageRecord.ConversationId"/>. Conversations are a local-store concept (there is no
/// conversation wire entity in the v1 protocol); membership (<see cref="Participants"/>) governs per-
/// conversation authorisation (SP-04, AUTH-FR-03) once identity lands in Phase 5.
/// </summary>
public sealed class Conversation
{
    /// <summary>Durable conversation ID (UUIDv4, assigned locally on creation).</summary>
    public string Id { get; set; } = null!;

    /// <summary>Optional human-readable title.</summary>
    public string? Title { get; set; }

    /// <summary>RFC 3339 UTC creation timestamp.</summary>
    public string CreatedAt { get; set; } = null!;

    /// <summary>RFC 3339 UTC last-modified timestamp.</summary>
    public string? UpdatedAt { get; set; }

    /// <summary>Membership rows linking participants to this conversation.</summary>
    public List<ConversationParticipant> Participants { get; set; } = new();
}
