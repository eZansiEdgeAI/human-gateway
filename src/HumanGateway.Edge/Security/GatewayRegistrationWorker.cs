using HumanGateway.Core.Time;
using HumanGateway.Edge.Sync;
using Microsoft.Extensions.Options;

namespace HumanGateway.Edge.Security;

/// <summary>
/// The Edge's gateway registration worker (AUTH-FR-01, SP-02): a hosted service that ensures the gateway is
/// REGISTERED with the Relay before sync begins, then keeps the registration token fresh by rotating it
/// before expiry. When no Relay is configured it is a no-op (offline-first, SP-01). Token bytes are never
/// logged (SP-07).
/// </summary>
/// <remarks>
/// <para>
/// Registration is a prerequisite for every Edge↔Relay exchange, so this worker is registered before the sync
/// worker and its first registration must complete before the first sync cycle (the sync worker additionally
/// gates on <see cref="GatewayIdentityManager.IsRegistered"/>). Transient Relay failures are retried with
/// capped, jittered exponential backoff so a school that boots during an outage still comes up registered the
/// moment the Relay is reachable — without hammering it.
/// </para>
/// <para>
/// The worker is single-threaded: only one handshake/rotation attempt runs at a time, serialised through the
/// manager, so request/confirm/rotate can never race (SP-02).
/// </para>
/// </remarks>
public sealed class GatewayRegistrationWorker : BackgroundService
{
    private readonly GatewayIdentityManager _identity;
    private readonly GatewayRegistrationClientGate _gate;
    private readonly IOptions<GatewayRegistrationWorkerOptions> _options;
    private readonly ILogger<GatewayRegistrationWorker> _logger;
    private readonly TimeProvider _time;

    /// <summary>Creates the worker over the identity manager, options, and logger.</summary>
    public GatewayRegistrationWorker(
        GatewayIdentityManager identity,
        GatewayRegistrationClientGate gate,
        IOptions<GatewayRegistrationWorkerOptions> options,
        ILogger<GatewayRegistrationWorker> logger,
        TimeProvider? time = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The identity manager's current cached identity (null before the first load/registration).</summary>
    public GatewayIdentity? CurrentIdentity => _identity.Current;

    /// <summary>True once the gateway is REGISTERED with the Relay (SP-02); surfaced for observability (NF-09).</summary>
    public bool IsRegistered => _identity.IsRegistered;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Gateway registration worker starting for gateway {GatewayId}",
            _identity.GatewayId);

        var first = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            var succeeded = false;
            try
            {
                await _gate.RunAsync(async ct =>
                {
                    var identity = await _identity.EnsureRegisteredAsync(ct).ConfigureAwait(false);

                    // Keep the token fresh: rotate when it expires within the configured horizon.
                    if (identity is { IsRegistered: true, TokenExpiresAt: { } expiry }
                        && ProtocolTime.TryParse(expiry, out var expiresAt)
                        && expiresAt - _time.GetUtcNow() < _options.Value.RotateWithin)
                    {
                        _logger.LogInformation(
                            "Gateway {GatewayId} registration token expires at {Expiry}; rotating now",
                            _identity.GatewayId, expiry);
                        await _identity.RotateTokenAsync(ct).ConfigureAwait(false);
                    }
                }, stoppingToken).ConfigureAwait(false);

                succeeded = true;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var delay = _options.Value.Backoff.ToPolicy().NextDelay(first ? 0 : 1);
                _logger.LogWarning(
                    ex,
                    "Gateway registration attempt failed ({FirstAttempt}); retrying in {RetryDelay}",
                    first ? "first" : "repeat", delay);
                await SafeDelayAsync(delay, stoppingToken).ConfigureAwait(false);
            }

            if (succeeded)
            {
                _logger.LogInformation("Gateway {GatewayId} registration is current; next check in {PollInterval}",
                    _identity.GatewayId, _options.Value.PollInterval);
                await SafeDelayAsync(_options.Value.PollInterval, stoppingToken).ConfigureAwait(false);
            }

            first = false;
        }

        _logger.LogInformation("Gateway registration worker stopping for gateway {GatewayId}", _identity.GatewayId);
    }

    /// <summary>Delays honouring cancellation (TimeProvider-aware so tests can fake time).</summary>
    private Task SafeDelayAsync(TimeSpan delay, CancellationToken ct)
        => Task.Delay(delay, _time, ct);
}
