using System.Text.Json;
using System.Text.Json.Serialization;
using HumanGateway.Protocol;

namespace HumanGateway.Edge.Api;

/// <summary>
/// JSON configuration for the Edge local REST API. Mirrors <c>ProtocolJson.Options</c> (exact enum tokens,
/// omit-null, disallow unmapped members) and additionally applies a camelCase naming policy so the local API's
/// plain request/response records share the protocol's wire convention without each needing an explicit
/// <c>[JsonPropertyName]</c>. Protocol entities are unaffected: their explicit <c>[JsonPropertyName]</c>
/// attributes override the policy, so an entity response is byte-identical to the protocol wire form.
/// </summary>
public static class LocalApiJson
{
    /// <summary>Applies the local API JSON contract to a serializer options instance.</summary>
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = false;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.Converters.Add(new ProtocolStringEnumConverter());
    }
}

/// <summary>Gateway identity/behaviour settings bound from the <c>Edge</c> configuration section.</summary>
public sealed class GatewayOptions
{
    public const string SectionName = "Edge";

    /// <summary>The durable gateway ID used as the outbox stream key (AUTH-FR-01).</summary>
    public string GatewayId { get; init; } = "edge:local";

    /// <summary>Human-readable gateway name (e.g. the school name), presented during registration (AUTH-FR-01).</summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Directory for durable runtime state (SQLite DB, artifact store, secret store). When null the Edge uses
    /// <c>&lt;ContentRoot&gt;/data</c> (SP-07: that path is gitignored).
    /// </summary>
    public string? DataDirectory { get; init; }
}
