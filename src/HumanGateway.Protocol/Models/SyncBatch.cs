using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace HumanGateway.Protocol.Models;

/// <summary>Sync direction: PUSH (gateway → Relay) or PULL (Relay → gateway).</summary>
public enum BatchDirection
{
    [EnumMember(Value = "PUSH")]
    Push,
    [EnumMember(Value = "PULL")]
    Pull,
}

/// <summary>Sync item kind — the discriminator of a SyncItem (oneOf, syncbatch.schema.json#/$defs/syncItem).</summary>
public enum SyncItemKind
{
    [EnumMember(Value = "message")]
    Message,
    [EnumMember(Value = "delivery")]
    Delivery,
    [EnumMember(Value = "artifact")]
    Artifact,
    [EnumMember(Value = "ack")]
    Ack,
}

/// <summary>Outcome reported back to the sender for a delivered message (SYNC-FR-05).</summary>
public enum DeliveryAckState
{
    [EnumMember(Value = "DELIVERED")]
    Delivered,
    [EnumMember(Value = "ACKNOWLEDGED")]
    Acknowledged,
    [EnumMember(Value = "FAILED")]
    Failed,
}

/// <summary>A delivery acknowledgement returned to the sender (syncbatch.schema.json#/$defs/deliveryAck, SYNC-FR-05).</summary>
public sealed record DeliveryAck
{
    /// <summary>The message being acknowledged.</summary>
    [JsonPropertyName("messageId")]
    public string MessageId { get; init; } = null!;

    /// <summary>The recipient that acknowledged delivery.</summary>
    [JsonPropertyName("recipient")]
    public Participant Recipient { get; init; } = null!;

    /// <summary>Outcome reported back to the sender (DELIVERED | ACKNOWLEDGED | FAILED).</summary>
    [JsonPropertyName("state")]
    public DeliveryAckState? State { get; init; }

    /// <summary>When the acknowledgement was recorded.</summary>
    [JsonPropertyName("acknowledgedAt")]
    public string AcknowledgedAt { get; init; } = null!;
}

/// <summary>
/// One unit of sync work, discriminated by <see cref="Kind"/>. Exactly one payload property
/// (<see cref="Message"/>, <see cref="Delivery"/>, <see cref="Artifact"/>, or <see cref="Ack"/>) must be
/// present and match <see cref="Kind"/>. Artifact items carry metadata only — the bytes travel over the
/// separate chunked artifact-transfer channel (PROTO-FR-04 exception).
/// </summary>
public sealed record SyncItem
{
    [JsonPropertyName("kind")]
    public SyncItemKind? Kind { get; init; }

    /// <summary>Per-gateway monotonic sequence number (≥ 1); the deterministic ordering key (SYNC-FR-07).</summary>
    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }

    [JsonPropertyName("message")]
    public Message? Message { get; init; }

    [JsonPropertyName("delivery")]
    public Delivery? Delivery { get; init; }

    [JsonPropertyName("artifact")]
    public Artifact? Artifact { get; init; }

    [JsonPropertyName("ack")]
    public DeliveryAck? Ack { get; init; }
}

/// <summary>
/// A cursor-based, idempotent batch of sync operations exchanged between an Edge Gateway and the Relay
/// (syncbatch.schema.json, SYNC-FR-03). Encodes the sync model: durable IDs, per-gateway sequence numbers,
/// opaque cursors, idempotency (batchId + idempotencyKey), and content hashes (SYNC-FR-01, SYNC-FR-02).
/// An empty <see cref="Items"/> array is a valid keepalive/cursor-advance batch (SYNC-FR-03).
/// </summary>
public sealed record SyncBatch
{
    /// <summary>Durable batch ID; a retry of the same batch MUST reuse the same batchId (idempotency, SYNC-FR-02).</summary>
    [JsonPropertyName("batchId")]
    public string BatchId { get; init; } = null!;

    /// <summary>The gateway that created this batch — the unique Edge Gateway identity (AUTH-FR-01).</summary>
    [JsonPropertyName("gatewayId")]
    public string GatewayId { get; init; } = null!;

    /// <summary>PUSH: gateway → Relay. PULL: Relay → gateway.</summary>
    [JsonPropertyName("direction")]
    public BatchDirection? Direction { get; init; }

    /// <summary>Opaque cursor from the receiving side for incremental sync; null on the first exchange (SYNC-FR-03).</summary>
    [JsonPropertyName("sinceCursor")]
    public string? SinceCursor { get; init; }

    /// <summary>Opaque cursor the receiver must store and return on the next exchange.</summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>Stable per-batch key; receiver deduplicates by batchId/idempotencyKey (SYNC-FR-02).</summary>
    [JsonPropertyName("idempotencyKey")]
    public string IdempotencyKey { get; init; } = null!;

    /// <summary>Smallest per-gateway sequence in this batch; null for an empty keepalive batch.</summary>
    [JsonPropertyName("sequenceStart")]
    public long? SequenceStart { get; init; }

    /// <summary>Largest per-gateway sequence in this batch; null for an empty keepalive batch.</summary>
    [JsonPropertyName("sequenceEnd")]
    public long? SequenceEnd { get; init; }

    /// <summary>The sync operations in this batch (≤ 1000). May be empty (keepalive, SYNC-FR-03).</summary>
    [JsonPropertyName("items")]
    public List<SyncItem>? Items { get; init; }

    /// <summary>When the batch was created.</summary>
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = null!;
}
