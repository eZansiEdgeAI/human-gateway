using System.Text.Json;
using System.Text.Json.Serialization;

namespace HumanGateway.Protocol.Models;

/// <summary>
/// JSON serialization contract for protocol entities.
/// </summary>
/// <remarks>
/// - Property wire names come from <c>[JsonPropertyName]</c> on every entity property (exact match to the
///   schema field names; case-sensitive on read).
/// - Enums map through <see cref="ProtocolStringEnumConverter"/> (exact wire tokens, both directions).
/// - <see cref="JsonIgnoreCondition.WhenWritingNull"/> keeps round-trips byte-identical: optional properties
///   that were absent stay absent when re-serialized.
/// - <see cref="JsonUnmappedMemberHandling.Disallow"/> rejects unknown properties, mirroring the schemas'
///   <c>additionalProperties: false</c> — this is what enforces PROTO-FR-04 (artifacts referenced, never
///   embedded) and the strict entity shapes at the wire boundary.
/// </remarks>
public static class ProtocolJson
{
    /// <summary>Options for (de)serializing protocol entities.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new ProtocolStringEnumConverter() },
    };
}
