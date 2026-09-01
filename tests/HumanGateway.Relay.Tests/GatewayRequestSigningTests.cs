using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Api;
using HumanGateway.Security;
using Xunit;

namespace HumanGateway.Relay.Tests;

/// <summary>
/// HTTP-level negative tests for the gateway request-signing boundary (IDENTITY-SECURITY-5.4, AUTH-FR-04,
/// SP-01): a registered gateway's signed traffic is accepted, while unsigned requests, signatures made with the
/// wrong key, stale timestamps, tampered request paths/queries, and identity-confusion attempts (a body or
/// query gatewayId that disagrees with the signed identity) are all rejected with 401 SIGNATURE_INVALID. Boots
/// the real Relay <c>Program</c> over Testcontainers PostgreSQL.
/// </summary>
public sealed class GatewayRequestSigningTests : IClassFixture<PostgresRelayFixture>
{
    private static readonly JsonSerializerOptions ApiJson = CreateApiJson();

    private readonly PostgresRelayFixture _fixture;

    public GatewayRequestSigningTests(PostgresRelayFixture fixture) => _fixture = fixture;

    private static JsonSerializerOptions CreateApiJson()
    {
        var options = new JsonSerializerOptions();
        RelayJson.Configure(options);
        return options;
    }

    // -----------------------------------------------------------------------------------------------
    // Positive: a signed request from a registered gateway is accepted
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task SignedPush_FromRegisteredGateway_IsAccepted()
    {
        using var factory = new RelayApiFactory(_fixture);
        var (gatewayId, key) = await RegisterGatewayAsync(factory);

        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, Url(client, "/sync/push"))
        {
            Content = JsonContent.Create(BuildKeepalive(gatewayId), mediaType: null, options: ApiJson),
        };
        var response = await SendSignedAsync(client, gatewayId, key, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // -----------------------------------------------------------------------------------------------
    // Negative: the auth boundary (all rejected with 401 SIGNATURE_INVALID)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task UnsignedRequest_IsRejected()
    {
        using var factory = new RelayApiFactory(_fixture);
        var (gatewayId, _) = await RegisterGatewayAsync(factory);

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/sync/push", BuildKeepalive(gatewayId), ApiJson);

        await AssertRejectedAsync(response);
    }

    [Fact]
    public async Task WrongKey_IsRejected()
    {
        using var factory = new RelayApiFactory(_fixture);
        var (gatewayId, _) = await RegisterGatewayAsync(factory);
        var wrongKey = GatewayRequestSigning.DeriveKey("hgrt_" + new string('Z', 43));

        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, Url(client, "/sync/push"))
        {
            Content = JsonContent.Create(BuildKeepalive(gatewayId), mediaType: null, options: ApiJson),
        };
        var response = await SendSignedAsync(client, gatewayId, wrongKey, request);

        await AssertRejectedAsync(response);
    }

    [Fact]
    public async Task StaleTimestamp_IsRejected()
    {
        using var factory = new RelayApiFactory(_fixture);
        var (gatewayId, key) = await RegisterGatewayAsync(factory);

        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, Url(client, "/sync/push"))
        {
            Content = JsonContent.Create(BuildKeepalive(gatewayId), mediaType: null, options: ApiJson),
        };
        var uri = request.RequestUri!;
        var staleTimestamp = GatewayRequestSigning.FormatTimestamp(DateTimeOffset.UtcNow.AddMinutes(-30));
        var nonce = GatewayRequestSigning.GenerateNonce();
        var canonical = GatewayRequestSigning.Canonicalize(
            request.Method.Method, uri.AbsolutePath, uri.Query, staleTimestamp, nonce, gatewayId);
        var signature = GatewayRequestSigning.Sign(key, canonical);
        request.Headers.TryAddWithoutValidation(GatewayRequestSigning.GatewayIdHeader, gatewayId);
        request.Headers.TryAddWithoutValidation(GatewayRequestSigning.TimestampHeader, staleTimestamp);
        request.Headers.TryAddWithoutValidation(GatewayRequestSigning.NonceHeader, nonce);
        request.Headers.TryAddWithoutValidation(GatewayRequestSigning.SignatureHeader, signature);

        var response = await client.SendAsync(request);

        await AssertRejectedAsync(response);
    }

    [Fact]
    public async Task TamperedPath_IsRejected()
    {
        using var factory = new RelayApiFactory(_fixture);
        var (gatewayId, key) = await RegisterGatewayAsync(factory);

        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, Url(client, "/sync/push"))
        {
            Content = JsonContent.Create(BuildKeepalive(gatewayId), mediaType: null, options: ApiJson),
        };
        // Signed for /sync/push, then the path is swapped to /sync/pull — the canonical no longer matches
        // the actual request the Relay recomputes.
        GatewayRequestSigning.SignRequest(request, gatewayId, key, TimeProvider.System);
        request.RequestUri = Url(client, "/sync/pull");

        var response = await client.SendAsync(request);

        await AssertRejectedAsync(response);
    }

    [Fact]
    public async Task QueryGatewayId_ClaimingAnotherRegisteredGateway_IsRejected()
    {
        using var factory = new RelayApiFactory(_fixture);
        var (gatewayId, key) = await RegisterGatewayAsync(factory);
        var (victimId, _) = await RegisterGatewayAsync(factory);

        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get,
            Url(client, $"/sync/artifacts/{HashOf("x")}/offset?gatewayId={victimId}"));
        // Signed as gatewayId, but the query claims a different registered gateway: the Relay attributes the
        // request to the victim and verifies the signature against the victim's key — a mismatch → 401.
        GatewayRequestSigning.SignRequest(request, gatewayId, key, TimeProvider.System);

        var response = await client.SendAsync(request);

        await AssertRejectedAsync(response);
    }

    [Fact]
    public async Task BodyGatewayId_ClaimingAnotherRegisteredGateway_IsRejected()
    {
        using var factory = new RelayApiFactory(_fixture);
        var (gatewayId, key) = await RegisterGatewayAsync(factory);
        var (victimId, _) = await RegisterGatewayAsync(factory);

        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, Url(client, "/sync/push"))
        {
            Content = JsonContent.Create(BuildKeepalive(victimId), mediaType: null, options: ApiJson),
        };
        // Signed as gatewayId, but the batch claims a different registered gateway → rejected at the victim's key.
        GatewayRequestSigning.SignRequest(request, gatewayId, key, TimeProvider.System);

        var response = await client.SendAsync(request);

        await AssertRejectedAsync(response);
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    private static async Task<(string GatewayId, string SigningKey)> RegisterGatewayAsync(RelayApiFactory factory)
    {
        var gatewayId = UniqueGatewayId("gateway:signing");
        using var client = factory.CreateClient();

        var issued = await client.PostAsJsonAsync("/gateways", new { gatewayId }, ApiJson);
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var token = (await issued.Content.ReadFromJsonAsync<RegistrationIssued>(ApiJson))!.RegistrationToken;

        var confirm = await client.PostAsJsonAsync($"/gateways/{gatewayId}/register",
            new { gatewayId, registrationToken = token }, ApiJson);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        return (gatewayId, GatewayRequestSigning.DeriveKey(token));
    }

    /// <summary>Signs a request (using the real production signing path) and sends it on the raw client.</summary>
    private static async Task<HttpResponseMessage> SendSignedAsync(
        HttpClient client, string gatewayId, string signingKey, HttpRequestMessage request)
    {
        GatewayRequestSigning.SignRequest(request, gatewayId, signingKey, TimeProvider.System);
        return await client.SendAsync(request);
    }

    /// <summary>Builds an absolute request URI from the factory client's base address (signing needs one).</summary>
    private static Uri Url(HttpClient client, string relative)
        => new(client.BaseAddress ?? throw new InvalidOperationException("Client has no base address."), relative);

    private static async Task AssertRejectedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.SignatureInvalid, error?.Code);
        Assert.False(error?.Retryable);
    }

    private static SyncBatch BuildKeepalive(string gatewayId) => new()
    {
        BatchId = "batch-" + Guid.NewGuid().ToString("N"),
        GatewayId = gatewayId,
        Direction = BatchDirection.Push,
        SinceCursor = null,
        Cursor = null,
        IdempotencyKey = "idem-" + Guid.NewGuid().ToString("N"),
        SequenceStart = null,
        SequenceEnd = null,
        Items = new List<SyncItem>(),
        CreatedAt = "2026-09-01T12:00:00.000Z",
    };

    private static string HashOf(string content) => HumanGateway.Core.Hashing.ContentHasher.Compute(
        System.Text.Encoding.UTF8.GetBytes(content));

    private static string UniqueGatewayId(string prefix)
        => $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}";
}
