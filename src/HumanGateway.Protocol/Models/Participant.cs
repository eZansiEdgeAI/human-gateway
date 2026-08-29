using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace HumanGateway.Protocol.Models;

/// <summary>Participant kind; must agree with the typed address prefix (PROTO-FR-02).</summary>
public enum ParticipantKind
{
    [EnumMember(Value = "human")]
    Human,
    [EnumMember(Value = "agent")]
    Agent,
    [EnumMember(Value = "system")]
    System,
}

/// <summary>
/// A typed participant: a <c>human:</c>, <c>agent:</c>, or <c>system:</c> address plus display metadata
/// (participant.schema.json, PROTO-FR-02). The only identity carried in message envelopes; HumanGateway
/// performs no consumer role-checking (SP-09). Optional <see cref="UserId"/> links human participants to a
/// local Edge User record; optional <see cref="GatewayId"/> links system participants to an Edge Gateway.
/// </summary>
public sealed record Participant
{
    /// <summary>Typed address, e.g. <c>human:teacher@school.example</c>. Prefix must match <see cref="Kind"/>.</summary>
    [JsonPropertyName("address")]
    public string Address { get; init; } = null!;

    /// <summary>Participant kind; must match the address prefix.</summary>
    [JsonPropertyName("kind")]
    public ParticipantKind? Kind { get; init; }

    /// <summary>Human-readable display name, cached from the last known metadata.</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = null!;

    /// <summary>Optional local user identifier for human participants (user.schema.json#/id, AUTH-FR-02).</summary>
    [JsonPropertyName("userId")]
    public string? UserId { get; init; }

    /// <summary>Optional Edge Gateway identity for system participants (gateway.schema.json#/gatewayId, AUTH-FR-01).</summary>
    [JsonPropertyName("gatewayId")]
    public string? GatewayId { get; init; }
}
