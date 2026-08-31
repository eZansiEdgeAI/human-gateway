using HumanGateway.Protocol.Models;

namespace HumanGateway.Edge.Api;

// -----------------------------------------------------------------------------------------------
// Request / response contracts for the Edge local REST API (EDGE-FR-03, product vision §6.3).
//
// The HTTP JSON layer uses a camelCase naming policy (LocalApiJson) so these plain records round-trip
// with the same wire convention as the protocol entities (which carry their own [JsonPropertyName]
// attributes and are therefore unaffected by the policy). Protocol entities are returned verbatim where
// the response is an entity; these DTOs exist only for the local API's request shapes and for views
// that combine entities with derived metadata (delivery status, message counts).
// -----------------------------------------------------------------------------------------------

/// <summary>A conversation plus its membership and derived activity metadata.</summary>
public sealed record ConversationView
{
    public string Id { get; init; } = null!;
    public string? Title { get; init; }
    public IReadOnlyList<Participant> Participants { get; init; } = Array.Empty<Participant>();
    public int MessageCount { get; init; }
    public string? LastMessageAt { get; init; }
    public string CreatedAt { get; init; } = null!;
}

/// <summary>A message envelope plus its per-recipient delivery records (PWA-FR-05).</summary>
public sealed record MessageView
{
    public Message Message { get; init; } = null!;
    public IReadOnlyList<Delivery> Deliveries { get; init; } = Array.Empty<Delivery>();
}

/// <summary>Request body for creating a conversation.</summary>
public sealed record CreateConversationRequest
{
    public string? Title { get; init; }
    public IReadOnlyList<Participant> Participants { get; init; } = Array.Empty<Participant>();
}

/// <summary>Request body for composing and sending a message (PWA-FR-04).</summary>
public sealed record SendMessageRequest
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

/// <summary>Request body for creating a human task (FLOW-FR-05, PWA-FR-06).</summary>
public sealed record CreateTaskRequest
{
    public HumanTaskKind? Kind { get; init; }
    public string WorkflowRef { get; init; } = null!;
    public string NodeId { get; init; } = null!;
    public string? Role { get; init; }
    public string Prompt { get; init; } = null!;
    public string? Subject { get; init; }
    public IReadOnlyList<string>? Options { get; init; }
    public string? CorrelationToken { get; init; }
    public string? ExpiresAt { get; init; }

    /// <summary>The system/agent participant that requested the task.</summary>
    public Participant Requester { get; init; } = null!;

    /// <summary>The human participants the task is delivered to.</summary>
    public IReadOnlyList<Participant> Assignees { get; init; } = Array.Empty<Participant>();

    /// <summary>Optional conversation to place the request/response messages in.</summary>
    public string? ConversationId { get; init; }
}

/// <summary>Request body for answering a human task (input or approval) (PWA-FR-06).</summary>
public sealed record AnswerTaskRequest
{
    public Participant RespondedBy { get; init; } = null!;
    public string? Text { get; init; }
    public ApprovalDecision? Decision { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<ArtifactReference>? ArtifactRefs { get; init; }
}

/// <summary>Request body for registering artifact metadata (bytes land via the artifact store, LOCAL-EDGE-1.5).</summary>
public sealed record RegisterArtifactRequest
{
    public string? Id { get; init; }
    public string Hash { get; init; } = null!;
    public long SizeBytes { get; init; }
    public string MimeType { get; init; } = null!;
    public string Filename { get; init; } = null!;
    public string? Description { get; init; }
}

/// <summary>Sync-status snapshot for the PWA sync banner (EDGE-FR-05, PWA-FR-05).</summary>
public sealed record SyncStatusView
{
    public string GatewayId { get; init; } = null!;
    public int Queued { get; init; }
    public long LastSequence { get; init; }
    public DeliverySummary Deliveries { get; init; } = new();
    public ArtifactSummary Artifacts { get; init; } = new();
}

/// <summary>
/// This gateway's artifact limits and current storage usage (ARTF-FR-03) — surfaced to the PWA so size-limit
/// and quota messages can be rendered before an upload is attempted.
/// </summary>
public sealed record ArtifactSummary
{
    /// <summary>Maximum bytes per artifact (per-gateway configurable).</summary>
    public long MaxSizeBytes { get; init; }

    /// <summary>Per-gateway storage quota in bytes.</summary>
    public long QuotaBytes { get; init; }

    /// <summary>Bytes currently stored across distinct content hashes.</summary>
    public long UsedBytes { get; init; }
}

/// <summary>Result of an artifact byte upload (PUT /artifacts/{id}/content).</summary>
public sealed record ArtifactUploadResult
{
    public string Id { get; init; } = null!;
    public string Hash { get; init; } = null!;
    public long SizeBytes { get; init; }

    /// <summary>True when bytes were newly written; false when identical bytes were already present (dedup).</summary>
    public bool Stored { get; init; }
}

/// <summary>Presence/limits snapshot for an artifact's bytes (GET /artifacts/{id}/content/status).</summary>
public sealed record ArtifactContentStatus
{
    public string Id { get; init; } = null!;
    public string Hash { get; init; } = null!;
    public bool Present { get; init; }
    public long StoredBytes { get; init; }
    public long MaxSizeBytes { get; init; }
    public long QuotaBytes { get; init; }
    public long QuotaUsedBytes { get; init; }
}

/// <summary>Counts of delivery records by lifecycle state (icon + text, never colour alone — ACC-03).</summary>
public sealed record DeliverySummary
{
    public int Queued { get; init; }
    public int Syncing { get; init; }
    public int Delivered { get; init; }
    public int Acknowledged { get; init; }
    public int WaitingForSync { get; init; }
    public int Failed { get; init; }
}
