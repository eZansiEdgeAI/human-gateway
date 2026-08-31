using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Api;
using HumanGateway.Relay.Security;
using Xunit;

namespace HumanGateway.Relay.Tests;

/// <summary>
/// HTTP-level integration tests for the gateway registration + rendezvous endpoints (CLOUD-RELAY-4.3,
/// RELAY-FR-03, WEBX-FR-02). Boots the real Relay <c>Program</c> over a Testcontainers PostgreSQL and
/// exercises the full surface over the wire, proving the acceptance criteria: the two-step registration
/// handshake returns the one-time token exactly once (SP-07), only REGISTERED gateways are rendezvous
/// targets (SP-02), and unregistered gateways are rejected (§7 #3).
/// </summary>
public sealed class GatewayRegistrationTests : IClassFixture<PostgresRelayFixture>
{
    /// <summary>
    /// The Relay's exact JSON contract (camelCase + ProtocolStringEnumConverter + omit-null + disallow
    /// unmapped) so reads assert the real wire form — <c>"status":"REGISTERED"</c>, exact camelCase keys.
    /// </summary>
    private static readonly System.Text.Json.JsonSerializerOptions ApiJson = CreateApiJson();

    private readonly RelayApiFactory _factory;

    public GatewayRegistrationTests(PostgresRelayFixture fixture)
    {
        _factory = new RelayApiFactory(fixture);
    }

    private static System.Text.Json.JsonSerializerOptions CreateApiJson()
    {
        var options = new System.Text.Json.JsonSerializerOptions();
        RelayJson.Configure(options);
        return options;
    }

    // -----------------------------------------------------------------------------------------------
    // Registration handshake (RELAY-FR-03, AUTH-FR-01)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task RequestRegistration_ReturnsOneTimeToken_AndPersistsOnlyFingerprint()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/gateways",
            new { gatewayId = "gateway:school-a", displayName = "Riverside Primary" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var issued = await response.Content.ReadFromJsonAsync<RegistrationIssued>(ApiJson);
        Assert.NotNull(issued);
        Assert.Equal("gateway:school-a", issued.GatewayId);
        Assert.Equal("PENDING", issued.Status);
        Assert.True(RegistrationTokens.IsWellFormed(issued.RegistrationToken),
            "the response carries the one-time plaintext token exactly once");
        Assert.NotNull(issued.TokenIssuedAt);
        Assert.NotNull(issued.TokenExpiresAt);

        // SP-07: the fingerprint must not appear anywhere in the response body.
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("fingerprint", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmRegistration_WithIssuedToken_MovesGatewayToRegistered()
    {
        using var client = _factory.CreateClient();
        var token = await IssueTokenAsync(client, "gateway:school-b");

        var response = await client.PostAsJsonAsync("/gateways/gateway:school-b/register",
            new { gatewayId = "gateway:school-b", registrationToken = token });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var gateway = await response.Content.ReadFromJsonAsync<Gateway>(ApiJson);
        Assert.NotNull(gateway);
        Assert.Equal("gateway:school-b", gateway.GatewayId);
        Assert.Equal(GatewayStatus.Registered, gateway.Status);
        Assert.NotNull(gateway.RegisteredAt);
        Assert.NotNull(gateway.LastSeenAt);
        Assert.StartsWith("sha256:", gateway.RegistrationTokenFingerprint);
    }

    [Fact]
    public async Task ConfirmRegistration_RejectsWrongToken_WithReservedErrorCode()
    {
        using var client = _factory.CreateClient();
        await IssueTokenAsync(client, "gateway:school-c");

        var response = await client.PostAsJsonAsync("/gateways/gateway:school-c/register",
            new { gatewayId = "gateway:school-c", registrationToken = "hgrt_" + new string('B', 43) });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.RegistrationTokenInvalid, error?.Code);
        Assert.False(error?.Retryable);
        Assert.DoesNotContain("hgrt_", error?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmRegistration_ForUnknownGateway_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/gateways/gateway:ghost/register",
            new { gatewayId = "gateway:ghost", registrationToken = "hgrt_" + new string('C', 43) });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RotateToken_ReplacesTheToken_AndOldTokenStopsWorking()
    {
        using var client = _factory.CreateClient();
        var oldToken = await IssueTokenAsync(client, "gateway:school-d");
        await ConfirmAsync(client, "gateway:school-d", oldToken);

        var rotateResponse = await client.PostAsJsonAsync("/gateways/gateway:school-d/rotate",
            new { gatewayId = "gateway:school-d", registrationToken = oldToken });
        Assert.Equal(HttpStatusCode.OK, rotateResponse.StatusCode);
        var rotated = await rotateResponse.Content.ReadFromJsonAsync<RegistrationIssued>(ApiJson);
        Assert.NotNull(rotated);
        Assert.NotEqual(oldToken, rotated.RegistrationToken);
        Assert.True(RegistrationTokens.IsWellFormed(rotated.RegistrationToken));

        // The old token is immediately invalid (SP-07 — the fingerprint was replaced).
        var reuse = await client.PostAsJsonAsync("/gateways/gateway:school-d/rotate",
            new { gatewayId = "gateway:school-d", registrationToken = oldToken });
        Assert.Equal(HttpStatusCode.Forbidden, reuse.StatusCode);
    }

    [Fact]
    public async Task DuplicateRegistration_ReturnsConflict()
    {
        using var client = _factory.CreateClient();
        await IssueTokenAsync(client, "gateway:school-e");

        // A second request for the same PENDING identity is a 409 (the previously issued token is
        // unrecoverable — only its fingerprint is stored — so re-requesting must not silently invalidate it).
        var again = await client.PostAsJsonAsync("/gateways", new { gatewayId = "gateway:school-e" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var error = await again.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.Conflict, error?.Code);
    }

    [Fact]
    public async Task RequestRegistration_RejectsMalformedPayload_WithValidationFailed()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/gateways", new { gatewayId = "nope!", displayName = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.ValidationFailed, error?.Code);
    }

    [Fact]
    public async Task Register_RouteBodyGatewayIdMismatch_IsRejected()
    {
        using var client = _factory.CreateClient();
        var token = await IssueTokenAsync(client, "gateway:school-f");

        // Route says school-f, body says school-g — defence-in-depth 400.
        var response = await client.PostAsJsonAsync("/gateways/gateway:school-f/register",
            new { gatewayId = "gateway:school-g", registrationToken = token });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -----------------------------------------------------------------------------------------------
    // Rendezvous (WEBX-FR-02, SP-02)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Rendezvous_ListContainsOnlyRegisteredGateways()
    {
        using var client = _factory.CreateClient();
        var token = await IssueTokenAsync(client, "gateway:school-g");
        await ConfirmAsync(client, "gateway:school-g", token);
        await IssueTokenAsync(client, "gateway:school-pending"); // PENDING, never confirmed

        var response = await client.GetAsync("/rendezvous/gateways");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var gateways = await response.Content.ReadFromJsonAsync<RendezvousGatewayInfo[]>(ApiJson);
        Assert.NotNull(gateways);
        Assert.Contains(gateways, g => g.GatewayId == "gateway:school-g" && g.Status == "REGISTERED");
        Assert.DoesNotContain(gateways, g => g.GatewayId == "gateway:school-pending");
    }

    [Fact]
    public async Task Rendezvous_GetGateway_RegisteredSucceeds_UnregisteredIs404()
    {
        using var client = _factory.CreateClient();
        var token = await IssueTokenAsync(client, "gateway:school-h");
        await ConfirmAsync(client, "gateway:school-h", token);

        var ok = await client.GetAsync("/rendezvous/gateways/gateway:school-h");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var info = await ok.Content.ReadFromJsonAsync<RendezvousGatewayInfo>(ApiJson);
        Assert.Equal("gateway:school-h", info?.GatewayId);
        Assert.True(info?.Online);

        var missing = await client.GetAsync("/rendezvous/gateways/gateway:never-heard-of-it");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Rendezvous_LookupSystemParticipant_ResolvesToServingGateway()
    {
        using var client = _factory.CreateClient();
        var token = await IssueTokenAsync(client, "gateway:school-i");
        await ConfirmAsync(client, "gateway:school-i", token);

        var response = await client.GetAsync("/rendezvous/lookup?participant=system:gateway:school-i");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lookup = await response.Content.ReadFromJsonAsync<RendezvousLookup>(ApiJson);
        Assert.Equal("system:gateway:school-i", lookup?.ParticipantAddress);
        Assert.Equal("gateway:school-i", lookup?.GatewayId);
        Assert.True(lookup?.Online);
    }

    [Fact]
    public async Task Rendezvous_LookupUnknownParticipant_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/rendezvous/lookup?participant=human:nobody@example.org");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.NotFound, error?.Code);
    }

    [Fact]
    public async Task Rendezvous_LookupMalformedParticipant_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/rendezvous/lookup?participant=not-a-typed-address");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.BadRequest, error?.Code);
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    private static async Task<string> IssueTokenAsync(
        HttpClient client, string gatewayId, HttpStatusCode expect = HttpStatusCode.Created)
    {
        var response = await client.PostAsJsonAsync("/gateways", new { gatewayId });
        Assert.Equal(expect, response.StatusCode);
        var issued = await response.Content.ReadFromJsonAsync<RegistrationIssued>(ApiJson);
        return issued!.RegistrationToken;
    }

    private static async Task ConfirmAsync(HttpClient client, string gatewayId, string token)
    {
        var response = await client.PostAsJsonAsync($"/gateways/{gatewayId}/register",
            new { gatewayId, registrationToken = token });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
