using HumanGateway.Core.Time;
using HumanGateway.Relay.Api;
using HumanGateway.Relay.Options;
using HumanGateway.Relay.Storage;
using HumanGateway.Relay.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HumanGateway.Relay.Services;

/// <summary>
/// Rendezvous routing for remote web access (RELAY-FR-03, WEBX-FR-02): resolves a remote participant's
/// request to the school Edge that serves them. Because the Edge is strictly outbound-only (SP-01), the
/// rendezvous never dials into the school — it tells remote clients which registered gateway to target and
/// whether it is currently reachable (recent <c>lastSeenAt</c>), and message delivery happens over the
/// gateway's next outbound sync pull. Only REGISTERED gateways are routable (SP-02).
/// </summary>
public sealed class RendezvousService
{
    private readonly IDbContextFactory<RelayDbContext> _factory;
    private readonly RelayOptions _options;

    public RendezvousService(IDbContextFactory<RelayDbContext> factory, IOptions<RelayOptions> options)
    {
        _factory = factory;
        _options = options.Value;
    }

    /// <summary>Lists every registered gateway as a rendezvous target (SP-02 filters the rest).</summary>
    public async Task<IReadOnlyList<RendezvousGatewayInfo>> ListGatewaysAsync(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var cutoff = Cutoff();
        var gateways = await db.Gateways
            .Where(g => g.Status == "REGISTERED")
            .OrderBy(g => g.GatewayId)
            .ToListAsync(ct);
        return gateways.Select(g => ToInfo(g, cutoff)).ToList();
    }

    /// <summary>Rendezvous info for one registered gateway, or null when absent/unregistered.</summary>
    public async Task<RendezvousGatewayInfo?> GetGatewayAsync(string gatewayId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = await db.Gateways.FirstOrDefaultAsync(g => g.GatewayId == gatewayId, ct);
        return record is { Status: "REGISTERED" } ? ToInfo(record, Cutoff()) : null;
    }

    /// <summary>
    /// Resolves a typed participant address to its serving gateway. <c>system:&lt;gatewayId&gt;</c> maps
    /// directly (PROTO-FR-02); <c>human:</c>/<c>agent:</c> addresses resolve through the participant
    /// directory's <c>gatewayId</c>. Returns null when the address is unknown or its gateway is not a
    /// registered rendezvous target (SP-02).
    /// </summary>
    public async Task<RendezvousLookup?> LookupParticipantAsync(string participantAddress, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        string? gatewayId = null;
        if (participantAddress.StartsWith("system:", StringComparison.Ordinal))
        {
            gatewayId = participantAddress["system:".Length..];
        }
        else
        {
            var participant = await db.Participants.FirstOrDefaultAsync(p => p.Address == participantAddress, ct);
            gatewayId = participant?.GatewayId;
        }

        if (gatewayId is null)
        {
            return null;
        }

        var gateway = await db.Gateways.FirstOrDefaultAsync(g => g.GatewayId == gatewayId, ct);
        if (gateway is not { Status: "REGISTERED" })
        {
            return null;
        }

        var cutoff = Cutoff();
        return new RendezvousLookup
        {
            ParticipantAddress = participantAddress,
            GatewayId = gateway.GatewayId,
            GatewayDisplayName = gateway.DisplayName,
            LastSeenAt = gateway.LastSeenAt,
            Online = IsOnline(gateway.LastSeenAt, cutoff),
        };
    }

    private static RendezvousGatewayInfo ToInfo(GatewayRecord gateway, DateTimeOffset cutoff) => new()
    {
        GatewayId = gateway.GatewayId,
        DisplayName = gateway.DisplayName,
        Status = "REGISTERED",
        LastSeenAt = gateway.LastSeenAt,
        Online = IsOnline(gateway.LastSeenAt, cutoff),
    };

    /// <summary>True when <paramref name="lastSeenAt"/> is within the online window of <paramref name="cutoff"/>.</summary>
    private static bool IsOnline(string? lastSeenAt, DateTimeOffset cutoff)
        => lastSeenAt is { } last && ProtocolTime.TryParse(last, out var when) && when >= cutoff;

    private DateTimeOffset Cutoff()
        => DateTimeOffset.UtcNow.AddMinutes(-_options.Rendezvous.OnlineWindowMinutes);
}
