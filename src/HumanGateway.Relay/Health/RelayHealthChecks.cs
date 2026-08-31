using System.Diagnostics;
using HumanGateway.Core.Time;
using HumanGateway.Relay.Options;
using HumanGateway.Relay.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HumanGateway.Relay.Health;

/// <summary>
/// Durable-store health check (NF-09): a real PostgreSQL round-trip (<c>CanConnect</c>), so the probe
/// reflects store availability, not just process liveness. Exposed to admins on <c>/health</c> and as the
/// liveness decision behind <c>/healthz</c> (RELAY-FR-05).
/// </summary>
public sealed class RelayStoreHealthCheck : IHealthCheck
{
    private readonly IDbContextFactory<RelayDbContext> _factory;

    /// <summary>Creates the check over the Relay's pooled-context factory.</summary>
    public RelayStoreHealthCheck(IDbContextFactory<RelayDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var connected = await db.Database.CanConnectAsync(ct);
            stopwatch.Stop();

            if (!connected)
            {
                return HealthCheckResult.Unhealthy("PostgreSQL is reachable as a process but rejects connections.");
            }

            return HealthCheckResult.Healthy(data: new Dictionary<string, object>
            {
                ["roundTripMs"] = stopwatch.ElapsedMilliseconds,
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return HealthCheckResult.Unhealthy(
                "PostgreSQL round-trip failed.", ex, new Dictionary<string, object>
                {
                    ["roundTripMs"] = stopwatch.ElapsedMilliseconds,
                });
        }
    }
}

/// <summary>
/// Sync-health check (NF-09: "sync health surfaced to admins"): reports the relay-side sync surface — how
/// many gateways are registered, how many are currently online (recent <c>lastSeenAt</c> within the
/// rendezvous online window, WEBX-FR-02), and how many undelivered cross-site items sit in the pull queues.
/// These numbers are the health signal; an empty system is healthy. The check degrades only when the store
/// becomes unreadable.
/// </summary>
public sealed class RelaySyncHealthCheck : IHealthCheck
{
    private readonly IDbContextFactory<RelayDbContext> _factory;
    private readonly RelayOptions _options;

    /// <summary>Creates the check over the context factory and relay behaviour options.</summary>
    public RelaySyncHealthCheck(IDbContextFactory<RelayDbContext> factory, IOptions<RelayOptions> options)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_options.Rendezvous.OnlineWindowMinutes);

            // Registered gateways + their rendezvous "online" watermark (LastSeenAt).
            var gateways = await db.Gateways
                .AsNoTracking()
                .Where(g => g.Status == "REGISTERED")
                .Select(g => g.LastSeenAt)
                .ToListAsync(ct);
            var registered = gateways.Count;
            var online = gateways.Count(lastSeen =>
                lastSeen is { } last && ProtocolTime.TryParse(last, out var when) && when >= cutoff);

            // Cross-site items queued for delivery but not yet pulled by their target gateway.
            var pending = await db.RelayOutbox
                .AsNoTracking()
                .CountAsync(e => e.DeliveredAtUtc == null, ct);

            return HealthCheckResult.Healthy(data: new Dictionary<string, object>
            {
                ["registeredGateways"] = registered,
                ["onlineGateways"] = online,
                ["pendingOutboxItems"] = pending,
            });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Sync health check failed — the store is unreadable.", ex);
        }
    }
}
