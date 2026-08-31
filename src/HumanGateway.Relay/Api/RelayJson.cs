using System.Text.Json;
using System.Text.Json.Serialization;
using HumanGateway.Protocol;

namespace HumanGateway.Relay.Api;

/// <summary>
/// JSON configuration for the Relay's HTTP API. Mirrors <c>ProtocolJson.Options</c> (exact enum tokens,
/// omit-null, disallow unmapped members) and additionally applies a camelCase naming policy so plain
/// request/response records share the protocol's wire convention without each needing an explicit
/// <c>[JsonPropertyName]</c>. Protocol entities are unaffected: their explicit <c>[JsonPropertyName]</c>
/// attributes override the policy, so an entity response is byte-identical to the protocol wire form.
/// </summary>
public static class RelayJson
{
    /// <summary>Applies the Relay API JSON contract to a serializer options instance.</summary>
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = false;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.Converters.Add(new ProtocolStringEnumConverter());
    }
}
