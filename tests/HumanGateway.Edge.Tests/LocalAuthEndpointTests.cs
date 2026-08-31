using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HumanGateway.Edge.Tests;

/// <summary>
/// HTTP-level integration tests for the Edge local user authentication (IDENTITY-SECURITY-5.2, AUTH-FR-02,
/// SP-03): username + password login issuing signed opaque session tokens, bearer-session identity, logout
/// revocation, account provisioning, and the seeded bootstrap user. Exercises the real Program over a
/// throwaway temp SQLite database (same harness as <see cref="LocalApiEndpointTests"/>).
/// </summary>
public sealed class LocalAuthEndpointTests : IClassFixture<LocalAuthEndpointTests.AuthFactory>
{
    private readonly AuthFactory _factory;

    public LocalAuthEndpointTests(AuthFactory factory) => _factory = factory;

    public sealed class AuthFactory : WebApplicationFactory<Program>
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "hgedge-auth-" + Guid.NewGuid().ToString("N"));

        public AuthFactory() => Directory.CreateDirectory(_dir);

        /// <summary>The seeded bootstrap credentials used by the tests.</summary>
        public (string Username, string Password) Bootstrap { get; } = ("bootstrap", "bootstrap-pw");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_dir, "edge.db"),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString();

            builder.UseSetting("ConnectionStrings:Edge", connectionString);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Artifacts:RootPath"] = Path.Combine(_dir, "artifacts"),
                    ["Auth:BootstrapUser:Username"] = Bootstrap.Username,
                    ["Auth:BootstrapUser:Password"] = Bootstrap.Password,
                    ["Auth:BootstrapUser:DisplayName"] = "Bootstrap User",
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try
            {
                if (Directory.Exists(_dir))
                {
                    Directory.Delete(_dir, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup.
            }
        }
    }

    // -----------------------------------------------------------------------------------------------
    // Bootstrap user (seeded from configuration at startup, SP-07)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Login_WithSeededBootstrapUser_Succeeds()
    {
        var client = _factory.CreateClient();

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
        var client = _factory.CreateClient();

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
    public async Task Login_UnknownUser_Returns401AuthRejected_WithoutRevealingAccountExistence()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/auth/login", JsonBody(new
        {
            username = "no-such-user",
            password = "whatever",
        }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AUTH_REJECTED", doc.RootElement.GetProperty("code").GetString());
    }

    // -----------------------------------------------------------------------------------------------
    // Session token lifecycle: /auth/me + /auth/logout
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Me_WithValidToken_ReturnsAuthenticatedUser()
    {
        var client = _factory.CreateClient();
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
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("UNAUTHORIZED", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Me_WithGarbageToken_Returns401()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesTheSession_SoSubsequentMeIsRejected()
    {
        var client = _factory.CreateClient();
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

    // -----------------------------------------------------------------------------------------------
    // Account provisioning (AUTH-FR-02)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task CreateUser_ThenLogin_Succeeds()
    {
        var client = _factory.CreateClient();

        var created = await client.PostAsync("/auth/users", JsonBody(new
        {
            username = "alice",
            displayName = "Alice Reviewer",
            password = "alice-password",
        }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using (var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
        {
            Assert.Equal("alice", doc.RootElement.GetProperty("username").GetString());
            Assert.Equal("Alice Reviewer", doc.RootElement.GetProperty("displayName").GetString());
            Assert.False(doc.RootElement.TryGetProperty("passwordVerifier", out _), "verifier must never be returned");
        }

        var token = await LoginAsync(client, "alice", "alice-password");
        Assert.StartsWith("hgsu_", token, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateUser_DuplicateUsername_Returns409()
    {
        var client = _factory.CreateClient();
        await client.PostAsync("/auth/users", JsonBody(new
        {
            username = "bob",
            displayName = "Bob",
            password = "bob-password",
        }));

        var duplicate = await client.PostAsync("/auth/users", JsonBody(new
        {
            username = "BOB", // case-insensitive duplicate
            displayName = "Bob Again",
            password = "other-password",
        }));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var doc = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        Assert.Equal("CONFLICT", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateUser_InvalidUsername_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/auth/users", JsonBody(new
        {
            username = "x", // too short for user.schema.json (min 3)
            displayName = "X",
            password = "x-password",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("VALIDATION_FAILED", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ListUsers_ReturnsSeededBootstrapUser()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/auth/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(doc.RootElement.EnumerateArray(), u =>
            u.GetProperty("username").GetString() == _factory.Bootstrap.Username);
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

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
