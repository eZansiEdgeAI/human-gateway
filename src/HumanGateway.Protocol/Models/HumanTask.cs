using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace HumanGateway.Protocol.Models;

/// <summary>Human task kind: human-input (free text / choice) or human-approval (approve / reject) (FLOW-FR-05).</summary>
public enum HumanTaskKind
{
    [EnumMember(Value = "input")]
    Input,
    [EnumMember(Value = "approval")]
    Approval,
}

/// <summary>Human task lifecycle state (product vision §10).</summary>
public enum HumanTaskStatus
{
    [EnumMember(Value = "REQUESTED")]
    Requested,
    [EnumMember(Value = "DELIVERED_TO_HUMAN")]
    DeliveredToHuman,
    [EnumMember(Value = "RESPONSE_RECEIVED")]
    ResponseReceived,
    [EnumMember(Value = "COMPLETED")]
    Completed,
    [EnumMember(Value = "EXPIRED")]
    Expired,
}

/// <summary>Approval decision (kind=approval).</summary>
public enum ApprovalDecision
{
    [EnumMember(Value = "approved")]
    Approved,
    [EnumMember(Value = "rejected")]
    Rejected,
}

/// <summary>The human's response to a task; populated once answered.</summary>
public sealed record TaskResponse
{
    /// <summary>Free-text answer (kind=input).</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>Approve / reject (kind=approval). Required when an approval task carries a response.</summary>
    [JsonPropertyName("decision")]
    public ApprovalDecision? Decision { get; init; }

    /// <summary>Optional reason accompanying an approval decision or an input answer.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>Evidence attached by the human, referenced — never embedded (PROTO-FR-04).</summary>
    [JsonPropertyName("artifactRefs")]
    public List<ArtifactReference>? ArtifactRefs { get; init; }

    /// <summary>The participant who answered.</summary>
    [JsonPropertyName("respondedBy")]
    public Participant? RespondedBy { get; init; }

    /// <summary>When the response was recorded.</summary>
    [JsonPropertyName("respondedAt")]
    public string? RespondedAt { get; init; }
}

/// <summary>
/// The workflow primitive transported by HumanGateway: a request for human input or approval, correlated to
/// the consumer workflow (humantask.schema.json, FLOW-FR-04/FLOW-FR-05). Lifecycle: REQUESTED →
/// DELIVERED_TO_HUMAN → RESPONSE_RECEIVED → COMPLETED, or EXPIRED. HumanGateway transports the task; the
/// consumer owns task semantics and authorisation (NG1, SP-09).
/// </summary>
public sealed record HumanTask
{
    /// <summary>Durable human task ID.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = null!;

    [JsonPropertyName("kind")]
    public HumanTaskKind? Kind { get; init; }

    [JsonPropertyName("status")]
    public HumanTaskStatus? Status { get; init; }

    /// <summary>Consumer workflow/run identifier; passed through unchanged.</summary>
    [JsonPropertyName("workflowRef")]
    public string WorkflowRef { get; init; } = null!;

    /// <summary>Workflow node that requested the interaction (FlowForge nodeId).</summary>
    [JsonPropertyName("nodeId")]
    public string NodeId { get; init; } = null!;

    /// <summary>Role required to answer; not enforced by HumanGateway (SP-09).</summary>
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    /// <summary>The question or instruction presented to the human.</summary>
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = null!;

    /// <summary>Short subject / title of the task.</summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    /// <summary>Choice options for kind=input prompts with a choice-style answer.</summary>
    [JsonPropertyName("options")]
    public List<string>? Options { get; init; }

    /// <summary>ID of the Message envelope carrying the task request.</summary>
    [JsonPropertyName("requestMessageId")]
    public string RequestMessageId { get; init; } = null!;

    /// <summary>ID of the Message envelope carrying the human's response, once answered.</summary>
    [JsonPropertyName("responseMessageId")]
    public string? ResponseMessageId { get; init; }

    /// <summary>The human's response; populated once answered.</summary>
    [JsonPropertyName("response")]
    public TaskResponse? Response { get; init; }

    /// <summary>Opaque consumer correlation token passed through unchanged (SP-09, AUTH-FR-06).</summary>
    [JsonPropertyName("correlationToken")]
    public string? CorrelationToken { get; init; }

    /// <summary>When set, the task transitions to EXPIRED if not completed by this time.</summary>
    [JsonPropertyName("expiresAt")]
    public string? ExpiresAt { get; init; }

    [JsonPropertyName("requestedAt")]
    public string? RequestedAt { get; init; }

    [JsonPropertyName("deliveredToHumanAt")]
    public string? DeliveredToHumanAt { get; init; }

    [JsonPropertyName("responseReceivedAt")]
    public string? ResponseReceivedAt { get; init; }

    [JsonPropertyName("completedAt")]
    public string? CompletedAt { get; init; }

    [JsonPropertyName("expiredAt")]
    public string? ExpiredAt { get; init; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = null!;

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; init; }
}
