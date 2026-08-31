using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace HumanGateway.Relay.Tests;

/// <summary>
/// HTTP-level integration tests for the Relay health endpoints (CLOUD-RELAY-4.7, NF-09: structured logging
/// + health endpoint; "sync health surfaced to admins"). Boots the real Relay <c>Program</c> over a
/// Testcontainers PostgreSQL and asserts:
///   - <c>/healthz</c> returns 200 while the durable store round-trip succeeds (compose probe contract);
///   - <c>/health</c> returns the detailed per-check report (store + sync) as documented JSON;
///   - the sync health report reflects registered/online gateways — the admin-visible sync surface.
/// </summary>
public sealed class HealthEndpointTests : IClassFixture<PostgresRelayFixture>
{
    private readonly RelayApiFactory _factory;

    public HealthEndpointTests(PostgresRelayFixture fixture)
    {
        _factory = new RelayApiFactory(fixture);
    }

    [Fact]
    public async Task Healthz_Returns200Ok_WhenStoreReachable()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("postgres", doc.RootElement.GetProperty("store").GetString());
    }

    [Fact]
    public async Task Health_ReturnsDetailedReport_WithStoreAndSyncChecks()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("generatedAt", out _), "the report carries a generatedAt timestamp");

        var checks = root.GetProperty("checks").EnumerateArray().ToList();
        var store = checks.Single(c => c.GetProperty("name").GetString() == "store");
        Assert.Equal("Healthy", store.GetProperty("status").GetString());
        Assert.True(store.GetProperty("data").TryGetProperty("roundTripMs", out _),
            "the store check reports its round-trip latency");

        var sync = checks.Single(c => c.GetProperty("name").GetString() == "sync");
        Assert.Equal("Healthy", sync.GetProperty("status").GetString());
        var syncData = sync.GetProperty("data");
        Assert.True(syncData.TryGetProperty("registeredGateways", out _));
        Assert.True(syncData.TryGetProperty("onlineGateways", out _));
        Assert.True(syncData.TryGetProperty("pendingOutboxItems", out _));
    }

    [Fact]
    public async Task Health_ReportsRegisteredAndOnlineGatewayCounts()
    {
        using var client = _factory.CreateClient();
        var token = await IssueTokenAsync(client, "gateway:health-sync-a");
        await ConfirmAsync(client, "gateway:health-sync-a", token);

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sync = doc.RootElement.GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "sync");
        var data = sync.GetProperty("data");

        // The freshly confirmed gateway is registered and within the rendezvous online window.
        Assert.Equal(1, data.GetProperty("registeredGateways").GetInt32());
        Assert.Equal(1, data.GetProperty("onlineGateways").GetInt32());
        Assert.True(data.GetProperty("pendingOutboxItems").GetInt32() >= 0);
    }

    [Fact]
    public async Task Healthz_ReflectsAnUnregisteredOnlySystem_AsHealthy()
    {
        // An empty Relay (no gateways yet) is a healthy system — the counts are the signal, not the verdict.
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    private static async Task<string> IssueTokenAsync(HttpClient client, string gatewayId)
    {
        var response = await client.PostAsJsonAsync("/gateways", new { gatewayId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("registrationToken").GetString()!;
    }

    private static async Task ConfirmAsync(HttpClient client, string gatewayId, string token)
    {
        var response = await client.PostAsJsonAsync($"/gateways/{gatewayId}/register",
            new { gatewayId, registrationToken = token });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
