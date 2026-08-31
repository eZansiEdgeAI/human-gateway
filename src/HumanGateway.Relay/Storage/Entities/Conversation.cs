namespace HumanGateway.Relay.Storage.Entities;

/// <summary>
/// A conversation as stored by the Relay — the shared grouping record that <see cref="MessageRecord"/>
/// envelopes reference by <see cref="MessageRecord.ConversationId"/>. Conversations are the rendezvous point
/// for cross-school exchange (RELAY-FR-04): messages from different gateways share a conversation through the
/// cloud. Membership (<see cref="Participants"/>) governs per-conversation authorisation (SP-04, AUTH-FR-03)
/// once identity lands in Phase 5.
/// </summary>
public sealed class Conversation
{
    /// <summary>Durable conversation ID — the primary key.</summary>
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
