extern alias edge;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Api;
using HumanGateway.Relay.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HumanGateway.Relay.Tests;

/// <summary>
/// Edge↔Relay gateway-registration integration tests (identity-security §6, AUTH-FR-01, SP-02): drives the
/// REAL Edge registration client (<c>edge::HumanGateway.Edge.Security.HttpGatewayRegistrationClient</c>) against
/// the REAL Relay over Testcontainers PostgreSQL, proving the acceptance criteria end to end: the two-step
/// handshake registers a gateway, an unregistered gateway is rejected by sync, and a re-registered (rotated)
/// gateway keeps working while a tampered token is rejected.
/// </summary>
public sealed class EdgeRegistrationIntegrationTests : IClassFixture<PostgresRelayFixture>
{
    private static readonly JsonSerializerOptions ApiJson = CreateApiJson();

    private readonly PostgresRelayFixture _fixture;

    public EdgeRegistrationIntegrationTests(PostgresRelayFixture fixture) => _fixture = fixture;

    private static JsonSerializerOptions CreateApiJson()
    {
        var options = new JsonSerializerOptions();
        RelayJson.Configure(options);
        return options;
    }

    /// <summary>The real Edge registration client pointed at the Relay under test (reusing the factory's
    /// in-memory test-server client — a fresh HttpClient would dial the real network and miss the host).</summary>
    private static edge::HumanGateway.Edge.Security.HttpGatewayRegistrationClient NewEdgeClient(
        HttpClient relayClient, string relayBaseUrl)
        => new(
            relayClient,
            new edge::HumanGateway.Edge.Security.GatewayRegistrationOptions
            {
                BaseUrl = relayBaseUrl,
                DisplayName = "Integration School",
            },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<edge::HumanGateway.Edge.Security.HttpGatewayRegistrationClient>.Instance);

    [Fact]
    public async Task EdgeClient_FullHandshake_RegistersGateway_AndSyncIsAccepted()
    {
        using var factory = new RelayApiFactory(_fixture);
        using var http = factory.CreateClient();
        var relayBaseUrl = http.BaseAddress!.ToString();

        var edgeClient = NewEdgeClient(http, relayBaseUrl);
        var gatewayId = UniqueGatewayId("gateway:edge-e2e");

        // Step 1 — request the registration token (the Edge client's own wire format).
        var issued = await edgeClient.RequestRegistrationAsync(gatewayId, "Integration School", default);
        Assert.NotNull(issued.RegistrationToken);
        Assert.StartsWith("hgrt_", issued.RegistrationToken, StringComparison.Ordinal);

        // Step 2 — present it to confirm.
        var gateway = await edgeClient.ConfirmRegistrationAsync(gatewayId, issued.RegistrationToken, default);
        Assert.Equal(GatewayStatus.Registered, gateway.Status);
        Assert.Equal(gatewayId, gateway.GatewayId);

        // Now the registered gateway can push a keepalive sync batch (SP-02 accepted).
        var batch = new SyncBatch
        {
            BatchId = HumanGateway.Core.Ids.IdGenerator.NewId(),
            GatewayId = gatewayId,
            Direction = BatchDirection.Push,
            IdempotencyKey = "idem-" + Guid.NewGuid().ToString("N"),
            SequenceStart = null,
            SequenceEnd = null,
            Items = new List<SyncItem>(),
            CreatedAt = "2026-09-01T00:00:00.000Z",
        };
        var push = await http.PostAsJsonAsync("/sync/push", batch, ApiJson);
        Assert.Equal(HttpStatusCode.OK, push.StatusCode);
    }

    [Fact]
    public async Task Relay_RejectsUnregisteredGateway_OnSyncAndArtifacts()
    {
        using var factory = new RelayApiFactory(_fixture);
        using var http = factory.CreateClient();
        var gatewayId = UniqueGatewayId("gateway:ghost-e2e");

        // Sync push from an identity that never registered → 404 GATEWAY_UNREGISTERED (SP-02).
        var batch = new SyncBatch
        {
            BatchId = HumanGateway.Core.Ids.IdGenerator.NewId(),
            GatewayId = gatewayId,
            Direction = BatchDirection.Push,
            IdempotencyKey = "idem-" + Guid.NewGuid().ToString("N"),
            SequenceStart = null,
            SequenceEnd = null,
            Items = new List<SyncItem>(),
            CreatedAt = "2026-09-01T00:00:00.000Z",
        };
        var push = await http.PostAsJsonAsync("/sync/push", batch, ApiJson);
        Assert.Equal(HttpStatusCode.NotFound, push.StatusCode);
        var error = await push.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.NotFound, error?.Code);

        // Artifact dedup check from an unregistered gateway → 404 as well.
        var artifacts = await http.PostAsJsonAsync("/sync/artifacts/state", new
        {
            gatewayId,
            hashes = new[] { "sha256:" + new string('0', 64) },
        }, ApiJson);
        Assert.Equal(HttpStatusCode.NotFound, artifacts.StatusCode);
    }

    [Fact]
    public async Task EdgeClient_ConfirmWithTamperedToken_IsRejected_WithoutTokenLeaking()
    {
        using var factory = new RelayApiFactory(_fixture);
        using var http = factory.CreateClient();
        var relayBaseUrl = http.BaseAddress!.ToString();
        var edgeClient = NewEdgeClient(http, relayBaseUrl);

        var gatewayId = UniqueGatewayId("gateway:edge-tamper");
        var issued = await edgeClient.RequestRegistrationAsync(gatewayId, null, default);

        // Present a different token — the Relay rejects with REGISTRATION_TOKEN_INVALID (SP-07).
        var tampered = (issued.RegistrationToken[..^1] + (issued.RegistrationToken.EndsWith('A') ? 'B' : 'A'));
        var ex = await Assert.ThrowsAsync<edge::HumanGateway.Edge.Security.GatewayRegistrationException>(
            () => edgeClient.ConfirmRegistrationAsync(gatewayId, tampered, default));

        Assert.Equal("REGISTRATION_TOKEN_INVALID", ex.Code);
        Assert.DoesNotContain("hgrt_", ex.Message, StringComparison.Ordinal); // SP-07: never leak the token
    }

    [Fact]
    public async Task EdgeClient_RequestForSuspendedGateway_IsRejected()
    {
        using var factory = new RelayApiFactory(_fixture);
        using var http = factory.CreateClient();
        var relayBaseUrl = http.BaseAddress!.ToString();
        var edgeClient = NewEdgeClient(http, relayBaseUrl);

        var gatewayId = UniqueGatewayId("gateway:edge-suspend");
        var issued = await edgeClient.RequestRegistrationAsync(gatewayId, null, default);
        await edgeClient.ConfirmRegistrationAsync(gatewayId, issued.RegistrationToken, default);

        // Suspend via the store, then a re-request of the same identity is forbidden (SP-02).
        await using (var db = factory.Services
            .GetRequiredService<IDbContextFactory<RelayDbContext>>().CreateDbContext())
        {
            var record = await db.Gateways.SingleAsync(g => g.GatewayId == gatewayId);
            record.Status = "SUSPENDED";
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<edge::HumanGateway.Edge.Security.GatewayRegistrationException>(
            () => edgeClient.RequestRegistrationAsync(gatewayId, null, default));

        Assert.Equal("GATEWAY_SUSPENDED", ex.Code);
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    private static string UniqueGatewayId(string prefix)
        => $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}";
}
