using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HumanGateway.Protocol.Models;

/// <summary>Message body rendering format.</summary>
public enum MessageFormat
{
    [EnumMember(Value = "plaintext")]
    Plaintext,
    [EnumMember(Value = "markdown")]
    Markdown,
}

/// <summary>Message payload: body text, optional rendering format, and optional structured data.</summary>
public sealed record MessagePayload
{
    /// <summary>Message text body (≤ 1,000,000 chars).</summary>
    [JsonPropertyName("body")]
    public string Body { get; init; } = null!;

    /// <summary>Body rendering format (schema default <c>plaintext</c>; absent stays absent on round-trip).</summary>
    [JsonPropertyName("format")]
    public MessageFormat? Format { get; init; }

    /// <summary>Optional structured data for agent/system messages.</summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }
}

/// <summary>
/// A durable message envelope — the communication primitive of HumanGateway
/// (message.schema.json, PROTO-FR-03). Artifacts are referenced by ID + hash, never embedded (PROTO-FR-04).
/// </summary>
public sealed record Message
{
    /// <summary>Durable message ID (SYNC-FR-01); never reused; receivers deduplicate on it.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = null!;

    /// <summary>The participant that created the message.</summary>
    [JsonPropertyName("sender")]
    public Participant Sender { get; init; } = null!;

    /// <summary>Intended recipients (1..64). Delivery state is tracked per recipient in the Delivery entity.</summary>
    [JsonPropertyName("recipients")]
    public List<Participant>? Recipients { get; init; }

    /// <summary>Conversation this message belongs to (membership governs authorisation, AUTH-FR-03).</summary>
    [JsonPropertyName("conversationId")]
    public string ConversationId { get; init; } = null!;

    /// <summary>Message ID this message replies to, if any.</summary>
    [JsonPropertyName("replyToMessageId")]
    public string? ReplyToMessageId { get; init; }

    /// <summary>Opaque consumer workflow/run identifier, not interpreted by HumanGateway.</summary>
    [JsonPropertyName("workflowRef")]
    public string? WorkflowRef { get; init; }

    /// <summary>ID of the HumanTask this message is the request or response for, if any.</summary>
    [JsonPropertyName("humanTaskId")]
    public string? HumanTaskId { get; init; }

    /// <summary>Message payload (body + optional format/data).</summary>
    [JsonPropertyName("payload")]
    public MessagePayload Payload { get; init; } = null!;

    /// <summary>References to attached artifacts (ID + hash + rendering metadata). Bytes are never embedded.</summary>
    [JsonPropertyName("artifactRefs")]
    public List<ArtifactReference>? ArtifactRefs { get; init; }

    /// <summary>Consumer correlation tokens passed through unchanged (SP-09, AUTH-FR-06).</summary>
    [JsonPropertyName("correlationTokens")]
    public Dictionary<string, string>? CorrelationTokens { get; init; }

    /// <summary>When the sender created the message (durable, set once).</summary>
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = null!;

    /// <summary>When the envelope last changed.</summary>
    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; init; }

    /// <summary>SHA-256 of the canonical JSON encoding of the envelope, excluding contentHash itself (SYNC-FR-02).</summary>
    [JsonPropertyName("contentHash")]
    public string ContentHash { get; init; } = null!;
}
