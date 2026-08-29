using HumanGateway.Core.Hashing;
using HumanGateway.Core.Ids;
using HumanGateway.Edge.Storage.Entities;
using HumanGateway.Protocol.Models;

namespace HumanGateway.Edge.Tests;

/// <summary>
/// Shared test fixtures for the Edge SQLite store tests. Mirrors the protocol shapes from
/// <c>HumanGateway.Core.Tests.TestData</c> but is local to the Edge test assembly.
/// </summary>
internal static class TestData
{
    public static readonly Participant Teacher = new()
    {
        Address = "human:teacher@school.example",
        Kind = ParticipantKind.Human,
        DisplayName = "Teacher",
        UserId = "user:teacher",
    };

    public static readonly Participant Assistant = new()
    {
        Address = "agent:assistant@school.example",
        Kind = ParticipantKind.Agent,
        DisplayName = "Assistant",
    };

    public static Message NewMessage(string? id = null, string? conversationId = null, string body = "hello")
    {
        var message = new Message
        {
            Id = id ?? IdGenerator.NewId(),
            Sender = Assistant,
            Recipients = new List<Participant> { Teacher },
            ConversationId = conversationId ?? IdGenerator.NewId(),
            Payload = new MessagePayload { Body = body, Format = MessageFormat.Plaintext },
            CreatedAt = "2026-08-29T00:00:00.000Z",
        };
        return message with { ContentHash = ContentHasher.ComputeMessageHash(message) };
    }

    public static Delivery NewDelivery(string messageId, Participant recipient, DeliveryState state = DeliveryState.Queued)
        => new()
        {
            Id = IdGenerator.NewId(),
            MessageId = messageId,
            Recipient = recipient,
            State = state,
            Attempts = 0,
            MaxAttempts = 5,
            QueuedAt = "2026-08-29T00:00:00.000Z",
            CreatedAt = "2026-08-29T00:00:00.000Z",
            UpdatedAt = "2026-08-29T00:00:00.000Z",
        };

    public static Artifact NewArtifact(string? id = null, string? hash = null, long sizeBytes = 12)
        => new()
        {
            Id = id ?? IdGenerator.NewId(),
            Hash = hash ?? "sha256:0000000000000000000000000000000000000000000000000000000000000000",
            SizeBytes = sizeBytes,
            MimeType = "application/pdf",
            Filename = "evidence.pdf",
            CreatedAt = "2026-08-29T00:00:00.000Z",
        };

    public static Conversation NewConversation(params Participant[] participants)
    {
        var now = "2026-08-29T00:00:00.000Z";
        var conversation = new Conversation
        {
            Id = IdGenerator.NewId(),
            Title = "Assessment",
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var participant in participants)
        {
            conversation.Participants.Add(new ConversationParticipant
            {
                ConversationId = conversation.Id,
                ParticipantAddress = participant.Address,
                JoinedAt = now,
            });
        }
        return conversation;
    }
}
