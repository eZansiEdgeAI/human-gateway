using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HumanGateway.Protocol;
using HumanGateway.Protocol.Models;

namespace HumanGateway.Edge.Security;

/// <summary>
/// The Edge's real outbound registration client (AUTH-FR-01, SP-02, SP-07): performs the two-step handshake
/// against the Relay's <c>POST /gateways</c> and <c>POST /gateways/{gatewayId}/register</c> endpoints, and
/// token rotation. Outbound-only (SP-01). The registration token is handled as a secret: it is never logged,
/// never included in exception messages, and never written to disk here — the identity manager persists it to
/// the secret store.
/// </summary>
public sealed class HttpGatewayRegistrationClient : IGatewayRegistrationClient
{
    private static readonly JsonSerializerOptions WireJson = CreateWireJson();

    private readonly HttpClient _http;
    private readonly GatewayRegistrationOptions _options;
    private readonly ILogger<HttpGatewayRegistrationClient> _logger;

    /// <summary>Creates the client over the given HTTP transport and options.</summary>
    public HttpGatewayRegistrationClient(
        HttpClient http,
        GatewayRegistrationOptions options,
        ILogger<HttpGatewayRegistrationClient> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsConfigured => _options.Enabled;

    /// <inheritdoc />
    public async Task<RegistrationTokenIssued> RequestRegistrationAsync(
        string gatewayId, string? displayName, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync("/gateways", new
        {
            gatewayId,
            displayName,
        }, WireJson, ct).ConfigureAwait(false);

        return await ReadIssuedAsync(response, "request registration", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Gateway> ConfirmRegistrationAsync(
        string gatewayId, string registrationToken, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"/gateways/{gatewayId}/register", new
        {
            gatewayId,
            registrationToken,
        }, WireJson, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await GatewayRegistrationException.FromResponseAsync(response, "confirm registration", ct)
                .ConfigureAwait(false);
        }

        var gateway = await response.Content.ReadFromJsonAsync<Gateway>(WireJson, ct).ConfigureAwait(false);
        return gateway ?? throw new GatewayRegistrationException(
            "The Relay returned an empty response while confirming registration.");
    }

    /// <inheritdoc />
    public async Task<RegistrationTokenIssued> RotateTokenAsync(
        string gatewayId, string currentToken, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"/gateways/{gatewayId}/rotate", new
        {
            gatewayId,
            registrationToken = currentToken,
        }, WireJson, ct).ConfigureAwait(false);

        return await ReadIssuedAsync(response, "rotate the registration token", ct).ConfigureAwait(false);
    }

    /// <summary>Reads a <c>RegistrationTokenIssued</c> response, translating Relay errors without leaking the token.</summary>
    private async Task<RegistrationTokenIssued> ReadIssuedAsync(
        HttpResponseMessage response, string action, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await GatewayRegistrationException.FromResponseAsync(response, action, ct).ConfigureAwait(false);
        }

        var issued = await response.Content.ReadFromJsonAsync<RegistrationTokenIssued>(WireJson, ct)
            .ConfigureAwait(false);
        if (issued is null || string.IsNullOrWhiteSpace(issued.RegistrationToken))
        {
            throw new GatewayRegistrationException(
                $"The Relay returned an invalid response while attempting to {action}.");
        }

        // SP-07: never log the token itself — only its presence and length.
        _logger.LogInformation("Gateway registration token issued (state {Status}, token length {TokenLength})",
            issued.Status, issued.RegistrationToken.Length);
        return issued;
    }

    private static JsonSerializerOptions CreateWireJson()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new ProtocolStringEnumConverter());
        return options;
    }
}
