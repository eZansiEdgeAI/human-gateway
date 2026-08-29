using HumanGateway.Core.Hashing;
using HumanGateway.Core.Ids;
using HumanGateway.Protocol.Models;

namespace HumanGateway.Core.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset FixedNow = new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

    public static readonly Participant Receiver = new()
    {
        Address = "human:teacher@school.example",
        Kind = ParticipantKind.Human,
        DisplayName = "Teacher",
    };

    public static readonly Participant Sender = new()
    {
        Address = "agent:assistant@school.example",
        Kind = ParticipantKind.Agent,
        DisplayName = "Assistant",
    };

    public static Participant Human(string address) => new()
    {
        Address = address,
        Kind = ParticipantKind.Human,
        DisplayName = address[(address.IndexOf(':') + 1)..],
    };

    public static Message NewMessage(string? id = null, string body = "hello", string? updatedAt = null)
    {
        var message = new Message
        {
            Id = id ?? IdGenerator.NewId(),
            Sender = Sender,
            Recipients = new List<Participant> { Receiver },
            ConversationId = "conversation:" + IdGenerator.NewId(),
            Payload = new MessagePayload { Body = body },
            CreatedAt = "2026-08-29T00:00:00.000Z",
            UpdatedAt = updatedAt,
        };
        return message with { ContentHash = ContentHasher.ComputeMessageHash(message) };
    }

    public static SyncItem MessageItem(Message message, long sequence)
        => new()
        {
            Kind = SyncItemKind.Message,
            Sequence = sequence,
            Message = message,
        };
}
