using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace HumanGateway.Protocol.Models;

/// <summary>
/// Delivery lifecycle state (PROTO-FR-05, product vision §10). <see cref="WaitingForSync"/> is a valid
/// state — offline-queued delivery is expected behaviour, never an error.
/// </summary>
public enum DeliveryState
{
    [EnumMember(Value = "QUEUED")]
    Queued,
    [EnumMember(Value = "SYNCING")]
    Syncing,
    [EnumMember(Value = "DELIVERED")]
    Delivered,
    [EnumMember(Value = "ACKNOWLEDGED")]
    Acknowledged,
    [EnumMember(Value = "WAITING_FOR_SYNC")]
    WaitingForSync,
    [EnumMember(Value = "FAILED")]
    Failed,
}

/// <summary>
/// Per-recipient delivery lifecycle record for a message (delivery.schema.json, PROTO-FR-05). The schema
/// validates snapshot consistency (e.g. FAILED requires <see cref="Error"/> and <see cref="FailedAt"/>);
/// transition legality is enforced by the sync engine.
/// </summary>
public sealed record Delivery
{
    /// <summary>Durable delivery record ID.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = null!;

    /// <summary>The message this delivery record tracks.</summary>
    [JsonPropertyName("messageId")]
    public string MessageId { get; init; } = null!;

    /// <summary>The recipient this delivery record applies to (one record per recipient).</summary>
    [JsonPropertyName("recipient")]
    public Participant Recipient { get; init; } = null!;

    /// <summary>Current delivery state.</summary>
    [JsonPropertyName("state")]
    public DeliveryState? State { get; init; }

    /// <summary>Completed sync/delivery attempts so far (≥ 0).</summary>
    [JsonPropertyName("attempts")]
    public long Attempts { get; init; }

    /// <summary>Maximum attempts before the state becomes FAILED (≥ 1).</summary>
    [JsonPropertyName("maxAttempts")]
    public long MaxAttempts { get; init; }

    /// <summary>Earliest time the next attempt may run (exponential backoff with jitter).</summary>
    [JsonPropertyName("nextRetryAt")]
    public string? NextRetryAt { get; init; }

    [JsonPropertyName("queuedAt")]
    public string? QueuedAt { get; init; }

    [JsonPropertyName("syncingAt")]
    public string? SyncingAt { get; init; }

    [JsonPropertyName("waitingForSyncAt")]
    public string? WaitingForSyncAt { get; init; }

    [JsonPropertyName("deliveredAt")]
    public string? DeliveredAt { get; init; }

    [JsonPropertyName("acknowledgedAt")]
    public string? AcknowledgedAt { get; init; }

    [JsonPropertyName("failedAt")]
    public string? FailedAt { get; init; }

    /// <summary>Failure details; required when state is FAILED.</summary>
    [JsonPropertyName("error")]
    public ProtocolError? Error { get; init; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = null!;

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; init; } = null!;
}
