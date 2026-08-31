using System.Text.Json;
using HumanGateway.Protocol.Models;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HumanGateway.Relay.Storage;

/// <summary>
/// Value-converter helpers that map protocol records to their canonical wire JSON for PostgreSQL jsonb storage
/// (RELAY-FR-01).
/// </summary>
/// <remarks>
/// Each stored record keeps its full protocol envelope as the canonical JSON produced by
/// <see cref="ProtocolJson.Options"/> (exact wire names, <c>additionalProperties: false</c>, enum tokens) so a
/// round-trip is byte-identical. Denormalised scalar columns alongside the JSON exist only for indexed
/// querying/ordering; the JSON column is the authoritative record.
/// </remarks>
public static class RelayJsonConversions
{
    /// <summary>
    /// A value converter that stores a protocol record as its canonical JSON and materialises it back.
    /// </summary>
    public static ValueConverter<TRecord, string> CanonicalJson<TRecord>()
        where TRecord : class
        => new(
            record => JsonSerializer.Serialize(record, ProtocolJson.Options),
            json => JsonSerializer.Deserialize<TRecord>(json, ProtocolJson.Options)!);

    /// <summary>
    /// Serialises an enum to its exact wire token (e.g. <c>REGISTERED</c>, <c>DELIVERED</c>) for use in a
    /// denormalised scalar query column. Mirrors <see cref="ProtocolJson.Options"/>' enum converter.
    /// </summary>
    public static string WireToken<TEnum>(TEnum value)
        where TEnum : struct, Enum
        => JsonSerializer.Serialize(value, ProtocolJson.Options).Trim('"');

    /// <summary>Serialises a nullable enum to its wire token, or null when absent.</summary>
    public static string? WireToken<TEnum>(TEnum? value)
        where TEnum : struct, Enum
        => value is null ? null : WireToken(value.Value);
}
