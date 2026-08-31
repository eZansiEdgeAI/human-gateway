using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HumanGateway.Relay.Tests;

/// <summary>
/// HTTP-level integration tests for the Relay remote user authentication (IDENTITY-SECURITY-5.2, AUTH-FR-02,
/// SP-03, external-web-access): the remote login gate. Username + password login issues signed opaque session
/// tokens, the bearer token resolves the identity, logout revokes, and the bootstrap user is seeded from
/// configuration (SP-07). Boots the real Relay <c>Program</c> over a Testcontainers PostgreSQL.
/// </summary>
public sealed class RemoteAuthEndpointTests : IClassFixture<PostgresRelayFixture>
{
    private readonly AuthRelayApiFactory _factory;

    public RemoteAuthEndpointTests(PostgresRelayFixture fixture)
    {
        _factory = new AuthRelayApiFactory(fixture);
    }

    /// <summary>Relay factory variant that also supplies the Auth bootstrap-user configuration (SP-07).</summary>
    public sealed class AuthRelayApiFactory : WebApplicationFactory<Program>
    {
        private readonly PostgresRelayFixture _fixture;

        public AuthRelayApiFactory(PostgresRelayFixture fixture) => _fixture = fixture;

        /// <summary>The seeded bootstrap credentials used by the tests.</summary>
        public (string Username, string Password) Bootstrap { get; } = ("remote.reviewer", "remote-pw");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Same precedence approach as RelayApiFactory: UseSetting wins over appsettings.Development.json.
            builder.UseSetting("ConnectionStrings:Relay", _fixture.Container.GetConnectionString());
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:BootstrapUser:Username"] = Bootstrap.Username,
                    ["Auth:BootstrapUser:Password"] = Bootstrap.Password,
                    ["Auth:BootstrapUser:DisplayName"] = "Remote Reviewer",
                });
            });
        }
    }

    [Fact]
    public async Task Login_WithSeededBootstrapUser_Succeeds()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/auth/login", JsonBody(new
        {
            username = _factory.Bootstrap.Username,
            password = _factory.Bootstrap.Password,
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = doc.RootElement.GetProperty("token").GetString();
        Assert.StartsWith("hgsu_", token, StringComparison.Ordinal);
        Assert.Equal(_factory.Bootstrap.Username, doc.RootElement.GetProperty("user").GetProperty("username").GetString());
        Assert.False(doc.RootElement.GetProperty("user").TryGetProperty("passwordVerifier", out _), "verifier must never be returned");
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401AuthRejected()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/auth/login", JsonBody(new
        {
            username = _factory.Bootstrap.Username,
            password = "wrong-password",
        }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AUTH_REJECTED", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsAuthenticatedRemoteUser()
    {
        using var client = _factory.CreateClient();
        var token = await LoginAsync(client, _factory.Bootstrap.Username, _factory.Bootstrap.Password);

        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(_factory.Bootstrap.Username, doc.RootElement.GetProperty("username").GetString());
        Assert.NotNull(doc.RootElement.GetProperty("expiresAt").GetString());
        Assert.False(doc.RootElement.TryGetProperty("passwordVerifier", out _), "verifier must never be returned");
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("UNAUTHORIZED", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Logout_RevokesTheSession_SoSubsequentMeIsRejected()
    {
        using var client = _factory.CreateClient();
        var token = await LoginAsync(client, _factory.Bootstrap.Username, _factory.Bootstrap.Password);

        var logout = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var logoutResponse = await client.SendAsync(logout);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var me = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var meResponse = await client.SendAsync(me);
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    [Fact]
    public async Task CreateRemoteUser_ThenLogin_Succeeds()
    {
        using var client = _factory.CreateClient();

        var created = await client.PostAsync("/auth/users", JsonBody(new
        {
            username = "alice.remote",
            displayName = "Alice Remote",
            password = "alice-password",
        }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var token = await LoginAsync(client, "alice.remote", "alice-password");
        Assert.StartsWith("hgsu_", token, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateRemoteUser_DuplicateUsername_Returns409()
    {
        using var client = _factory.CreateClient();
        await client.PostAsync("/auth/users", JsonBody(new
        {
            username = "bob.remote",
            displayName = "Bob",
            password = "bob-password",
        }));

        var duplicate = await client.PostAsync("/auth/users", JsonBody(new
        {
            username = "BOB.REMOTE", // case-insensitive duplicate
            displayName = "Bob Again",
            password = "other-password",
        }));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var doc = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        Assert.Equal("CONFLICT", doc.RootElement.GetProperty("code").GetString());
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsync("/auth/login", JsonBody(new { username, password }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    private static StringContent JsonBody(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}
