using HumanGateway.Edge.Api;
using HumanGateway.Edge.Security;
using HumanGateway.Protocol.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HumanGateway.Edge.Tests;

/// <summary>
/// Identity-manager tests (AUTH-FR-01, SP-02, SP-07): the two-step registration handshake is orchestrated
/// correctly — request a token, persist PENDING, confirm, persist REGISTERED — and every transition is
/// resumable from the durable secret store across crashes/restarts. Also verifies the offline-first path
/// (no Relay configured → stays Unregistered, never touches the network) and that the registration token is
/// never logged (SP-07).
/// </summary>
public sealed class GatewayIdentityManagerTests
{
    private const string GatewayId = "gateway:school-tests";

    private static GatewayIdentityManager NewManager(
        FakeRegistrationClient client,
        IGatewaySecretStore? store = null,
        GatewayOptions? gateway = null)
        => new(
            store ?? new InMemorySecretStore(),
            client,
            Options.Create(gateway ?? new GatewayOptions { GatewayId = GatewayId, DisplayName = "Test School" }),
            NullLogger<GatewayIdentityManager>.Instance);

    /// <summary>In-memory <see cref="IGatewaySecretStore"/> recording every save.</summary>
    private sealed class InMemorySecretStore : IGatewaySecretStore
    {
        public GatewayIdentity? Stored { get; set; }

        public int SaveCount { get; private set; }

        public Task<GatewayIdentity?> LoadAsync(CancellationToken ct = default) => Task.FromResult(Stored);

        public Task SaveAsync(GatewayIdentity identity, CancellationToken ct = default)
        {
            Stored = identity;
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    /// <summary>Configurable fake <see cref="IGatewayRegistrationClient"/> recording every call.</summary>
    private sealed class FakeRegistrationClient : IGatewayRegistrationClient
    {
        private readonly string _issuedToken;

        public FakeRegistrationClient(
            bool configured = true,
            string? issuedToken = null,
            GatewayStatus? confirmStatus = GatewayStatus.Registered,
            Func<string, CancellationToken, Task>? onRequest = null,
            Func<string, string, CancellationToken, Task>? onConfirm = null)
        {
            IsConfigured = configured;
            _issuedToken = issuedToken ?? "hgrt_" + new string('T', 43);
            ConfirmStatus = confirmStatus;
            OnRequest = onRequest;
            OnConfirm = onConfirm;
        }

        public bool IsConfigured { get; }

        public GatewayStatus? ConfirmStatus { get; }

        public Func<string, CancellationToken, Task>? OnRequest { get; }

        public Func<string, string, CancellationToken, Task>? OnConfirm { get; }

        public int RequestCalls { get; private set; }

        public int ConfirmCalls { get; private set; }

        public int RotateCalls { get; private set; }

        public string? LastRequestedDisplayName { get; private set; }

        public Task<RegistrationTokenIssued> RequestRegistrationAsync(
            string gatewayId, string? displayName, CancellationToken ct)
        {
            RequestCalls++;
            LastRequestedDisplayName = displayName;
            if (OnRequest is not null)
            {
                OnRequest(gatewayId, ct).GetAwaiter().GetResult();
            }

            return Task.FromResult(new RegistrationTokenIssued
            {
                GatewayId = gatewayId,
                Status = "PENDING",
                RegistrationToken = _issuedToken,
                TokenIssuedAt = "2026-09-01T00:00:00.000Z",
                TokenExpiresAt = "2026-10-01T00:00:00.000Z",
            });
        }

        public Task<Gateway> ConfirmRegistrationAsync(string gatewayId, string registrationToken, CancellationToken ct)
        {
            ConfirmCalls++;
            if (OnConfirm is not null)
            {
                OnConfirm(gatewayId, registrationToken, ct).GetAwaiter().GetResult();
            }

            return Task.FromResult(new Gateway
            {
                GatewayId = gatewayId,
                Status = ConfirmStatus,
                CreatedAt = "2026-09-01T00:00:00.000Z",
            });
        }

        public Task<RegistrationTokenIssued> RotateTokenAsync(string gatewayId, string currentToken, CancellationToken ct)
        {
            RotateCalls++;
            return Task.FromResult(new RegistrationTokenIssued
            {
                GatewayId = gatewayId,
                Status = "REGISTERED",
                RegistrationToken = "hgrt_" + new string('R', 43),
                TokenIssuedAt = "2026-09-02T00:00:00.000Z",
                TokenExpiresAt = "2026-10-02T00:00:00.000Z",
            });
        }
    }

    // -----------------------------------------------------------------------------------------------
    // First registration (no stored identity)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task EnsureRegistered_WhenNoStoredIdentity_RunsFullHandshake_AndPersistsRegistered()
    {
        var store = new InMemorySecretStore();
        var client = new FakeRegistrationClient();
        var manager = NewManager(client, store);

        var identity = await manager.EnsureRegisteredAsync();

        Assert.True(identity.IsRegistered);
        Assert.Equal(GatewayIdentityState.Registered, identity.State);
        Assert.Equal(GatewayId, identity.GatewayId);
        Assert.Equal("hgrt_" + new string('T', 43), identity.RegistrationToken);
        Assert.NotNull(identity.RegisteredAtUtc);

        // Step 1 (request) then step 2 (confirm) — exactly once each.
        Assert.Equal(1, client.RequestCalls);
        Assert.Equal(1, client.ConfirmCalls);

        // The display name is forwarded to the Relay.
        Assert.Equal("Test School", client.LastRequestedDisplayName);

        // The token was persisted after the request step AND after the confirm step (resumable PENDING state).
        Assert.Equal(2, store.SaveCount);
        Assert.Equal(GatewayIdentityState.Registered, store.Stored!.State);

        // The manager caches the identity.
        Assert.Same(identity, manager.Current);
    }

    [Fact]
    public async Task EnsureRegistered_WhenAlreadyRegistered_DoesNotTouchTheNetwork()
    {
        var store = new InMemorySecretStore();
        store.Stored = new GatewayIdentity
        {
            GatewayId = GatewayId,
            State = GatewayIdentityState.Registered,
            RegistrationToken = "hgrt_" + new string('T', 43),
            TokenExpiresAt = "2026-10-01T00:00:00.000Z",
            RegisteredAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var client = new FakeRegistrationClient();
        var manager = NewManager(client, store);

        var identity = await manager.EnsureRegisteredAsync();

        Assert.True(identity.IsRegistered);
        Assert.Equal(0, client.RequestCalls);
        Assert.Equal(0, client.ConfirmCalls);
        Assert.Equal(0, store.SaveCount); // no persistence churn
    }

    // -----------------------------------------------------------------------------------------------
    // Resumable handshake (crash between steps)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task EnsureRegistered_WhenStoredPendingToken_ResumesByConfirmingWithoutReRequesting()
    {
        var store = new InMemorySecretStore();
        store.Stored = new GatewayIdentity
        {
            GatewayId = GatewayId,
            State = GatewayIdentityState.Pending,
            RegistrationToken = "hgrt_" + new string('T', 43),
            TokenExpiresAt = "2026-10-01T00:00:00.000Z",
        };
        var client = new FakeRegistrationClient();
        var manager = NewManager(client, store);

        var identity = await manager.EnsureRegisteredAsync();

        Assert.True(identity.IsRegistered);
        Assert.Equal(0, client.RequestCalls);   // no new token requested
        Assert.Equal(1, client.ConfirmCalls);   // the stored token is presented
        Assert.Equal("hgrt_" + new string('T', 43), identity.RegistrationToken);
    }

    // -----------------------------------------------------------------------------------------------
    // Rejections
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task EnsureRegistered_WhenRelayDoesNotConfirm_ThrowsAndDoesNotPersistRegistered()
    {
        var store = new InMemorySecretStore();
        var client = new FakeRegistrationClient(confirmStatus: GatewayStatus.Suspended);
        var manager = NewManager(client, store);

        var ex = await Assert.ThrowsAsync<GatewayRegistrationException>(() => manager.EnsureRegisteredAsync());

        Assert.Equal("GATEWAY_UNREGISTERED", ex.Code);
        // The PENDING state was persisted after the request step, but never REGISTERED.
        Assert.Equal(GatewayIdentityState.Pending, store.Stored!.State);
    }

    [Fact]
    public async Task EnsureRegistered_WhenRequestFails_DoesNotPersistAnything()
    {
        var store = new InMemorySecretStore();
        var client = new FakeRegistrationClient(
            onRequest: (_, _) => throw new GatewayRegistrationException(
                "The Relay rejected the attempt to request registration (HTTP 403, GATEWAY_SUSPENDED).",
                System.Net.HttpStatusCode.Forbidden, code: "GATEWAY_SUSPENDED"));
        var manager = NewManager(client, store);

        var ex = await Assert.ThrowsAsync<GatewayRegistrationException>(() => manager.EnsureRegisteredAsync());

        Assert.Equal("GATEWAY_SUSPENDED", ex.Code);
        Assert.Null(store.Stored); // nothing persisted — a suspended gateway must never get a stored token
    }

    // -----------------------------------------------------------------------------------------------
    // Offline-first (no Relay configured)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task EnsureRegistered_WhenNoRelayConfigured_StaysUnregistered_WithoutNetworkCalls()
    {
        var store = new InMemorySecretStore();
        var client = new FakeRegistrationClient(configured: false);
        var manager = NewManager(client, store);

        var identity = await manager.EnsureRegisteredAsync();

        Assert.False(identity.IsRegistered);
        Assert.Equal(GatewayIdentityState.Unregistered, identity.State);
        Assert.Equal(0, client.RequestCalls);
        Assert.Equal(0, client.ConfirmCalls);
        Assert.Null(store.Stored);
    }

    [Fact]
    public async Task EnsureRegistered_WhenNoRelayConfigured_KeepsExistingLocalIdentityUnchanged()
    {
        var store = new InMemorySecretStore();
        store.Stored = new GatewayIdentity
        {
            GatewayId = GatewayId,
            State = GatewayIdentityState.Unregistered,
            RegistrationToken = null,
        };
        var client = new FakeRegistrationClient(configured: false);
        var manager = NewManager(client, store);

        var identity = await manager.EnsureRegisteredAsync();

        Assert.Equal(GatewayIdentityState.Unregistered, identity.State);
        Assert.Equal(0, client.RequestCalls);
    }

    // -----------------------------------------------------------------------------------------------
    // Rotation
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task RotateToken_PersistsTheFreshToken_AndCachesIt()
    {
        var store = new InMemorySecretStore();
        store.Stored = new GatewayIdentity
        {
            GatewayId = GatewayId,
            State = GatewayIdentityState.Registered,
            RegistrationToken = "hgrt_" + new string('T', 43),
            TokenExpiresAt = "2026-10-01T00:00:00.000Z",
            RegisteredAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var client = new FakeRegistrationClient();
        var manager = NewManager(client, store);

        var rotated = await manager.RotateTokenAsync();

        Assert.Equal(1, client.RotateCalls);
        Assert.Equal("hgrt_" + new string('R', 43), rotated.RegistrationToken);
        Assert.Equal("2026-10-02T00:00:00.000Z", rotated.TokenExpiresAt);
        Assert.Equal("hgrt_" + new string('R', 43), store.Stored!.RegistrationToken);
        Assert.Same(rotated, manager.Current);
    }

    [Fact]
    public async Task RotateToken_WhenNotRegistered_Throws()
    {
        var store = new InMemorySecretStore(); // nothing stored
        var client = new FakeRegistrationClient();
        var manager = NewManager(client, store);

        var ex = await Assert.ThrowsAsync<GatewayRegistrationException>(() => manager.RotateTokenAsync());

        Assert.Contains("not registered", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, client.RotateCalls);
    }

    // -----------------------------------------------------------------------------------------------
    // Token secrecy (SP-07)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task EnsureRegistered_TokenIsNeverWrittenToExceptions()
    {
        var store = new InMemorySecretStore();
        var client = new FakeRegistrationClient(
            onConfirm: (_, token, _) => throw new GatewayRegistrationException(
                "The Relay rejected the attempt to confirm registration (HTTP 403, REGISTRATION_TOKEN_INVALID).",
                System.Net.HttpStatusCode.Forbidden, code: "REGISTRATION_TOKEN_INVALID"));
        var manager = NewManager(client, store);

        var ex = await Assert.ThrowsAsync<GatewayRegistrationException>(() => manager.EnsureRegisteredAsync());

        Assert.DoesNotContain("hgrt_", ex.Message, StringComparison.Ordinal);
    }
}
