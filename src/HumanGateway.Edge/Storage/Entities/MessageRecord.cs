using HumanGateway.Protocol.Models;

namespace HumanGateway.Edge.Storage.Entities;

/// <summary>
/// Durable local storage of a protocol <see cref="Message"/> envelope (EDGE-FR-02). The full envelope is kept
/// as canonical JSON in <see cref="Envelope"/>; the scalar columns are denormalised for indexed querying and
/// ordering (by conversation, by sender, chronologically).
/// </summary>
public sealed class MessageRecord
{
    /// <summary>Durable message ID — the primary key (SYNC-FR-01).</summary>
    public string Id { get; set; } = null!;

    /// <summary>The conversation this message belongs to (indexed for listing).</summary>
    public string ConversationId { get; set; } = null!;

    /// <summary>The sender participant's typed address (indexed).</summary>
    public string SenderAddress { get; set; } = null!;

    /// <summary>RFC 3339 UTC creation timestamp (indexed for chronological ordering).</summary>
    public string CreatedAt { get; set; } = null!;

    /// <summary>The envelope content hash (SYNC-FR-02) for integrity verification.</summary>
    public string ContentHash { get; set; } = null!;

    /// <summary>The full protocol envelope, stored as canonical wire JSON.</summary>
    public Message Envelope { get; set; } = null!;

    /// <summary>Creates a storage record from a protocol envelope, deriving the query columns.</summary>
    public static MessageRecord FromEnvelope(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new MessageRecord
        {
            Id = message.Id,
            ConversationId = message.ConversationId,
            SenderAddress = message.Sender.Address,
            CreatedAt = message.CreatedAt,
            ContentHash = message.ContentHash,
            Envelope = message,
        };
    }
}
