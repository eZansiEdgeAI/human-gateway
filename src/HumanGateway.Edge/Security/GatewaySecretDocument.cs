using System.Text.Json;
using System.Text.Json.Serialization;
using HumanGateway.Protocol;

namespace HumanGateway.Edge.Security;

/// <summary>
/// JSON contract for the Edge secret store (SP-07). The file holds the gateway identity including the
/// plaintext registration token; it is written with owner-only permissions and must never be committed to a
/// repo (<c>src/HumanGateway.Edge/data/</c> is gitignored). Explicit <c>[JsonPropertyName]</c> attributes
/// keep the format stable and independent of any naming policy.
/// </summary>
internal static class GatewaySecretJson
{
    /// <summary>Serializer options for the secret store document.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ProtocolStringEnumConverter() },
    };
}

/// <summary>
/// The on-disk shape of the Edge secret store (gateway-identity.json): the gateway ID, its registration
/// lifecycle state, and the plaintext registration token (SP-07).
/// </summary>
internal sealed record GatewaySecretDocument
{
    [JsonPropertyName("gatewayId")]
    public string GatewayId { get; init; } = null!;

    [JsonPropertyName("state")]
    public GatewayIdentityState State { get; init; }

    [JsonPropertyName("registrationToken")]
    public string? RegistrationToken { get; init; }

    [JsonPropertyName("tokenExpiresAt")]
    public string? TokenExpiresAt { get; init; }

    [JsonPropertyName("registeredAtUtc")]
    public DateTimeOffset? RegisteredAtUtc { get; init; }

    /// <summary>Projects the persisted document to the domain identity record.</summary>
    public GatewayIdentity ToIdentity() => new()
    {
        GatewayId = GatewayId,
        State = State,
        RegistrationToken = RegistrationToken,
        TokenExpiresAt = TokenExpiresAt,
        RegisteredAtUtc = RegisteredAtUtc,
    };

    /// <summary>Builds the persisted document from the domain identity record.</summary>
    public static GatewaySecretDocument From(GatewayIdentity identity) => new()
    {
        GatewayId = identity.GatewayId,
        State = identity.State,
        RegistrationToken = identity.RegistrationToken,
        TokenExpiresAt = identity.TokenExpiresAt,
        RegisteredAtUtc = identity.RegisteredAtUtc,
    };
}
