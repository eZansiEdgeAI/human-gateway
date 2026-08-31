using System.Net;
using System.Text;
using System.Text.Json;
using HumanGateway.Edge.Security;
using HumanGateway.Protocol;
using HumanGateway.Protocol.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HumanGateway.Edge.Tests;

/// <summary>
/// Wire-format tests for the Edge's outbound registration client (AUTH-FR-01, SP-02, SP-07): the two-step
/// handshake and rotation send the exact Relay wire contract (camelCase bodies, correct endpoints), parse the
/// Relay's responses, and translate rejections into <see cref="GatewayRegistrationException"/> without ever
/// leaking the registration token into exception messages (SP-07).
/// </summary>
public sealed class HttpGatewayRegistrationClientTests
{
    private static readonly JsonSerializerOptions WireJson = CreateWireJson();

    private static JsonSerializerOptions CreateWireJson()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new ProtocolStringEnumConverter());
        return options;
    }

    private static HttpGatewayRegistrationClient NewClient(StubHandler handler)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("https://relay.example.test/") },
            new GatewayRegistrationOptions { BaseUrl = "https://relay.example.test", DisplayName = "Riverside" },
            NullLogger<HttpGatewayRegistrationClient>.Instance);

    /// <summary>Configurable <see cref="HttpMessageHandler"/> that records the request and returns canned responses.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Response);
        }
    }

    private static HttpRequestMessage Request(string method, string relativeUrl)
        => new(new HttpMethod(method), new Uri("https://relay.example.test" + relativeUrl));

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body)
    {
        var json = JsonSerializer.Serialize(body, WireJson);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    // -----------------------------------------------------------------------------------------------
    // Step 1 — request registration (POST /gateways)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task RequestRegistration_PostsCamelCaseBody_AndParsesIssuedToken()
    {
        var handler = new StubHandler
        {
            Response = JsonResponse(HttpStatusCode.Created, new
            {
                gatewayId = "gateway:school-a",
                status = "PENDING",
                registrationToken = "hgrt_" + new string('A', 43),
                tokenIssuedAt = "2026-09-01T00:00:00.000Z",
                tokenExpiresAt = "2026-10-01T00:00:00.000Z",
            }),
        };
        var client = NewClient(handler);

        var issued = await client.RequestRegistrationAsync("gateway:school-a", "Riverside", default);

        Assert.Equal("gateway:school-a", issued.GatewayId);
        Assert.Equal("PENDING", issued.Status);
        Assert.Equal("hgrt_" + new string('A', 43), issued.RegistrationToken);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/gateways", handler.LastRequest.RequestUri!.AbsolutePath);

        // The body must be camelCase: gatewayId + displayName.
        var body = await handler.LastRequest.Content!.ReadAsStringAsync();
        Assert.Contains("\"gatewayId\":\"gateway:school-a\"", body);
        Assert.Contains("\"displayName\":\"Riverside\"", body);
    }

    [Fact]
    public async Task RequestRegistration_WhenRelayRejects_ThrowsWithCode_WithoutTokenInMessage()
    {
        var handler = new StubHandler
        {
            Response = JsonResponse(HttpStatusCode.Forbidden, new
            {
                code = "GATEWAY_SUSPENDED",
                message = "The gateway is suspended (SP-02).",
                retryable = false,
            }),
        };
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<GatewayRegistrationException>(
            () => client.RequestRegistrationAsync("gateway:school-a", null, default));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Equal("GATEWAY_SUSPENDED", ex.Code);
        Assert.False(ex.Retryable);
        Assert.DoesNotContain("hgrt_", ex.Message, StringComparison.Ordinal); // SP-07
    }

    [Fact]
    public async Task RequestRegistration_WhenRelayConflicts_ReportsConflictCode()
    {
        var handler = new StubHandler
        {
            Response = JsonResponse(HttpStatusCode.Conflict, new
            {
                code = "CONFLICT",
                message = "already registered",
            }),
        };
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<GatewayRegistrationException>(
            () => client.RequestRegistrationAsync("gateway:school-a", null, default));

        Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
        Assert.Equal("CONFLICT", ex.Code);
    }

    // -----------------------------------------------------------------------------------------------
    // Step 2 — confirm registration (POST /gateways/{gatewayId}/register)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmRegistration_PostsToken_AndParsesRegisteredGateway()
    {
        var handler = new StubHandler
        {
            Response = JsonResponse(HttpStatusCode.OK, new
            {
                gatewayId = "gateway:school-b",
                status = "REGISTERED",
                registeredAt = "2026-09-01T00:00:00.000Z",
                createdAt = "2026-09-01T00:00:00.000Z",
            }),
        };
        var client = NewClient(handler);

        var gateway = await client.ConfirmRegistrationAsync("gateway:school-b", "hgrt_" + new string('B', 43), default);

        Assert.Equal("gateway:school-b", gateway.GatewayId);
        Assert.Equal(GatewayStatus.Registered, gateway.Status);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/gateways/gateway:school-b/register", handler.LastRequest.RequestUri!.AbsolutePath);

        var body = await handler.LastRequest.Content!.ReadAsStringAsync();
        Assert.Contains("\"gatewayId\":\"gateway:school-b\"", body);
        Assert.Contains("\"registrationToken\":\"hgrt_", body);
    }

    [Fact]
    public async Task ConfirmRegistration_WhenTokenInvalid_ThrowsRegistrationTokenInvalid()
    {
        var handler = new StubHandler
        {
            Response = JsonResponse(HttpStatusCode.Forbidden, new
            {
                code = "REGISTRATION_TOKEN_INVALID",
                message = "The registration token is invalid (SP-07).",
            }),
        };
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<GatewayRegistrationException>(
            () => client.ConfirmRegistrationAsync("gateway:school-b", "hgrt_" + new string('X', 43), default));

        Assert.Equal("REGISTRATION_TOKEN_INVALID", ex.Code);
        Assert.DoesNotContain("hgrt_", ex.Message, StringComparison.Ordinal); // SP-07
    }

    // -----------------------------------------------------------------------------------------------
    // Rotation (POST /gateways/{gatewayId}/rotate)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task RotateToken_PostsCurrentToken_AndReturnsFreshOne()
    {
        var handler = new StubHandler
        {
            Response = JsonResponse(HttpStatusCode.OK, new
            {
                gatewayId = "gateway:school-c",
                status = "REGISTERED",
                registrationToken = "hgrt_" + new string('C', 43),
                tokenIssuedAt = "2026-09-01T00:00:00.000Z",
                tokenExpiresAt = "2026-10-01T00:00:00.000Z",
            }),
        };
        var client = NewClient(handler);

        var issued = await client.RotateTokenAsync("gateway:school-c", "hgrt_" + new string('D', 43), default);

        Assert.Equal("hgrt_" + new string('C', 43), issued.RegistrationToken);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/gateways/gateway:school-c/rotate", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task RotateToken_WhenCurrentTokenExpired_ThrowsExpiredCode()
    {
        var handler = new StubHandler
        {
            Response = JsonResponse(HttpStatusCode.Forbidden, new
            {
                code = "REGISTRATION_TOKEN_EXPIRED",
                message = "The registration token has expired.",
            }),
        };
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<GatewayRegistrationException>(
            () => client.RotateTokenAsync("gateway:school-c", "hgrt_" + new string('E', 43), default));

        Assert.Equal("REGISTRATION_TOKEN_EXPIRED", ex.Code);
    }

    // -----------------------------------------------------------------------------------------------
    // Network-level failures
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task RequestRegistration_WhenRelayUnreachable_SurfacesAsException()
    {
        var handler = new StubHandler { Response = new HttpResponseMessage(HttpStatusCode.BadGateway) };
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<GatewayRegistrationException>(
            () => client.RequestRegistrationAsync("gateway:school-a", null, default));

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
    }

    [Fact]
    public void IsConfigured_ReflectsRelayBaseUrlPresence()
    {
        Assert.False(new HttpGatewayRegistrationClient(
                new HttpClient(),
                new GatewayRegistrationOptions { BaseUrl = null },
                NullLogger<HttpGatewayRegistrationClient>.Instance)
            .IsConfigured);

        Assert.True(new HttpGatewayRegistrationClient(
                new HttpClient(),
                new GatewayRegistrationOptions { BaseUrl = "https://relay.example.test" },
                NullLogger<HttpGatewayRegistrationClient>.Instance)
            .IsConfigured);
    }
}
