namespace HumanGateway.Relay.Storage.Entities;

/// <summary>
/// Join row for conversation membership at the Relay (a participant's presence in a conversation). Membership
/// governs per-conversation authorisation (SP-04, AUTH-FR-03): a participant may only read/write conversations
/// they are a member of.
/// </summary>
public sealed class ConversationParticipant
{
    /// <summary>The conversation this membership applies to.</summary>
    public string ConversationId { get; set; } = null!;

    /// <summary>The participant's typed address (e.g. <c>human:teacher@school.example</c>).</summary>
    public string ParticipantAddress { get; set; } = null!;

    /// <summary>RFC 3339 UTC timestamp when the participant joined.</summary>
    public string JoinedAt { get; set; } = null!;

    /// <summary>Navigation to the owning conversation.</summary>
    public Conversation? Conversation { get; set; }

    /// <summary>Navigation to the participant directory record.</summary>
    public ParticipantRecord? Participant { get; set; }
}
