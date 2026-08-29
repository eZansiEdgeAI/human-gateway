using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace HumanGateway.Protocol.Models;

/// <summary>Gateway registration lifecycle (gateway.schema.json, AUTH-FR-01). Only REGISTERED gateways may exchange sync batches.</summary>
public enum GatewayStatus
{
    [EnumMember(Value = "UNREGISTERED")]
    Unregistered,
    [EnumMember(Value = "PENDING")]
    Pending,
    [EnumMember(Value = "REGISTERED")]
    Registered,
    [EnumMember(Value = "SUSPENDED")]
    Suspended,
    [EnumMember(Value = "REVOKED")]
    Revoked,
}

/// <summary>
/// Edge Gateway identity and registration record (gateway.schema.json, AUTH-FR-01, SP-02, SP-07). The Relay
/// stores only a SHA-256 fingerprint of the registration token; the Edge holds the plaintext token in its
/// secret store — never in code or repos (SP-07).
/// </summary>
public sealed record Gateway
{
    /// <summary>Unique durable gateway identity (SP-02). Referenced by syncbatch.gatewayId and system: participant suffix.</summary>
    [JsonPropertyName("gatewayId")]
    public string GatewayId { get; init; } = null!;

    /// <summary>Human-readable gateway name (e.g. the school or site name).</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>Registration lifecycle state.</summary>
    [JsonPropertyName("status")]
    public GatewayStatus? Status { get; init; }

    /// <summary>SHA-256 of the current registration token, as stored by the Relay (SP-07).</summary>
    [JsonPropertyName("registrationTokenFingerprint")]
    public string? RegistrationTokenFingerprint { get; init; }

    [JsonPropertyName("tokenIssuedAt")]
    public string? TokenIssuedAt { get; init; }

    [JsonPropertyName("tokenExpiresAt")]
    public string? TokenExpiresAt { get; init; }

    [JsonPropertyName("registeredAt")]
    public string? RegisteredAt { get; init; }

    /// <summary>Required when status is SUSPENDED.</summary>
    [JsonPropertyName("suspendedAt")]
    public string? SuspendedAt { get; init; }

    /// <summary>Required when status is REVOKED.</summary>
    [JsonPropertyName("revokedAt")]
    public string? RevokedAt { get; init; }

    /// <summary>Last successful authenticated Edge↔Relay exchange.</summary>
    [JsonPropertyName("lastSeenAt")]
    public string? LastSeenAt { get; init; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = null!;

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; init; }
}
