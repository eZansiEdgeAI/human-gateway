using HumanGateway.Edge.Api;
using HumanGateway.Edge.Security;
using HumanGateway.Edge.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HumanGateway.Edge.Tests;

/// <summary>
/// Registration-worker tests (AUTH-FR-01, SP-02): the hosted service ensures the gateway registers with the
/// Relay on startup (and re-checks on the poll interval), retries transient failures with backoff instead of
/// crashing, and is a no-op when no Relay is configured (offline-first, SP-01). A tiny poll interval keeps
/// the tests fast.
/// </summary>
public sealed class GatewayRegistrationWorkerTests
{
    private const string GatewayId = "gateway:school-worker";

    private static GatewayIdentityManager NewManager(
        IGatewayRegistrationClient client,
        GatewayIdentity? stored = null)
    {
        var store = new MemoryStore(stored);
        return new GatewayIdentityManager(
            store,
            client,
            Options.Create(new GatewayOptions { GatewayId = GatewayId, DisplayName = "Worker School" }),
            NullLogger<GatewayIdentityManager>.Instance);
    }

    private sealed class MemoryStore : IGatewaySecretStore
    {
        public MemoryStore(GatewayIdentity? stored) => Stored = stored;

        public GatewayIdentity? Stored { get; private set; }

        public Task<GatewayIdentity?> LoadAsync(CancellationToken ct = default) => Task.FromResult(Stored);

        public Task SaveAsync(GatewayIdentity identity, CancellationToken ct = default)
        {
            Stored = identity;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClient : IGatewayRegistrationClient
    {
        public bool IsConfigured { get; init; } = true;

        public int RequestCalls { get; private set; }

        public int ConfirmCalls { get; private set; }

        public int FailuresBeforeSuccess { get; set; }

        public Task<RegistrationTokenIssued> RequestRegistrationAsync(
            string gatewayId, string? displayName, CancellationToken ct)
        {
            RequestCalls++;
            return Task.FromResult(new RegistrationTokenIssued
            {
                GatewayId = gatewayId,
                Status = "PENDING",
                RegistrationToken = "hgrt_" + new string('W', 43),
                TokenIssuedAt = "2026-09-01T00:00:00.000Z",
                TokenExpiresAt = "2026-10-01T00:00:00.000Z",
            });
        }

        public Task<HumanGateway.Protocol.Models.Gateway> ConfirmRegistrationAsync(
            string gatewayId, string registrationToken, CancellationToken ct)
        {
            if (ConfirmCalls < FailuresBeforeSuccess)
            {
                ConfirmCalls++;
                throw new GatewayRegistrationException(
                    "The Relay is temporarily unreachable (HTTP 503).",
                    System.Net.HttpStatusCode.ServiceUnavailable, retryable: true);
            }

            ConfirmCalls++;
            return Task.FromResult(new HumanGateway.Protocol.Models.Gateway
            {
                GatewayId = gatewayId,
                Status = HumanGateway.Protocol.Models.GatewayStatus.Registered,
                CreatedAt = "2026-09-01T00:00:00.000Z",
            });
        }

        public Task<RegistrationTokenIssued> RotateTokenAsync(
            string gatewayId, string currentToken, CancellationToken ct)
            => throw new InvalidOperationException("Not expected in these tests.");
    }

    private static GatewayRegistrationWorker NewWorker(
        GatewayIdentityManager manager,
        TimeSpan pollInterval)
    {
        var options = Options.Create(new GatewayRegistrationWorkerOptions
        {
            PollIntervalSeconds = (int)pollInterval.TotalSeconds,
            RotateWithinSeconds = 60,
        });
        return new GatewayRegistrationWorker(
            manager,
            new GatewayRegistrationClientGate(),
            options,
            NullLogger<GatewayRegistrationWorker>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_WhenUnregistered_RegistersWithTheRelay()
    {
        var client = new FakeClient();
        var worker = NewWorker(NewManager(client, stored: null), TimeSpan.FromSeconds(1));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await worker.StartAsync(cts.Token);
        try
        {
            await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected: the worker runs until cancellation.
        }

        // The first cycle registered the gateway (the fake store has the token).
        Assert.True(client.RequestCalls >= 1);
        Assert.True(client.ConfirmCalls >= 1);
        Assert.True(worker.IsRegistered,
            "the identity manager caches the registered identity after the worker's first cycle");
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAlreadyRegistered_DoesNotReRegister()
    {
        var client = new FakeClient();
        var stored = new GatewayIdentity
        {
            GatewayId = GatewayId,
            State = GatewayIdentityState.Registered,
            RegistrationToken = "hgrt_" + new string('S', 43),
            TokenExpiresAt = "2026-10-01T00:00:00.000Z",
            RegisteredAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var manager = NewManager(client, stored);
        var worker = NewWorker(manager, TimeSpan.FromSeconds(1));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await worker.StartAsync(cts.Token);
        try
        {
            await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        Assert.Equal(0, client.RequestCalls);
        Assert.Equal(0, client.ConfirmCalls);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoRelayConfigured_IsANoOp()
    {
        var client = new FakeClient { IsConfigured = false };
        var worker = NewWorker(NewManager(client, stored: null), TimeSpan.FromSeconds(1));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await worker.StartAsync(cts.Token);
        try
        {
            await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        Assert.Equal(0, client.RequestCalls);
        Assert.Equal(0, client.ConfirmCalls);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRelayTransientlyFails_RetriesUntilRegistered()
    {
        var client = new FakeClient { FailuresBeforeSuccess = 1 };
        var worker = NewWorker(NewManager(client, stored: null), TimeSpan.FromSeconds(1));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(cts.Token);
        try
        {
            await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        Assert.True(client.ConfirmCalls >= 2, "the worker retried after the transient failure");
        Assert.True(worker.IsRegistered);
        await worker.StopAsync(CancellationToken.None);
    }
}
