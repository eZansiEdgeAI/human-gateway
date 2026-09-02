using HumanGateway.Protocol.Models;

namespace HumanGateway.Relay.Api;

/// <summary>Authenticated remote-user message submission (WEBX-FR-02).</summary>
public sealed record RemoteMessageRequest
{
    public Participant Sender { get; init; } = null!;
    public IReadOnlyList<Participant> Recipients { get; init; } = Array.Empty<Participant>();
    public string ConversationId { get; init; } = null!;
    public string? ReplyToMessageId { get; init; }
    public string? WorkflowRef { get; init; }
    public string? HumanTaskId { get; init; }
    public MessagePayload Payload { get; init; } = null!;
    public IReadOnlyList<ArtifactReference>? ArtifactRefs { get; init; }
    public Dictionary<string, string>? CorrelationTokens { get; init; }
}
