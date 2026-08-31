using System.Net;
using HumanGateway.Core.Time;
using HumanGateway.Edge.Api;
using HumanGateway.Protocol.Models;
using Microsoft.Extensions.Options;

namespace HumanGateway.Edge.Security;

/// <summary>
/// Owns the Edge Gateway's registration lifecycle (AUTH-FR-01, SP-02): loads the persisted identity from the
/// secret store, drives the two-step registration handshake against the Relay when needed, and caches the
/// current identity in memory. Every transition is persisted atomically before the next step, so a crash
/// between steps resumes from the durable state (PENDING → confirm) instead of re-registering.
/// </summary>
/// <remarks>
/// <para>
/// Single-threaded by design: the registration worker (hosted service) is the only caller. The manager is
/// registered as a singleton; the worker serialises handshake attempts with its own gate so no concurrent
/// request/confirm/rotate races can occur.
/// </para>
/// <para>
/// The registration token is handled as a secret end-to-end (SP-07): it is read from the secret store,
/// sent to the Relay over TLS, and persisted back to the secret store. It is never logged, never written to
/// configuration, and never included in exceptions.
/// </para>
/// </remarks>
public sealed class GatewayIdentityManager
{
    private readonly IGatewaySecretStore _secretStore;
    private readonly IGatewayRegistrationClient _registrationClient;
    private readonly IOptions<GatewayOptions> _gateway;
    private readonly ILogger<GatewayIdentityManager> _logger;
    private readonly TimeProvider _time;

    private GatewayIdentity? _cached;

    /// <summary>Creates the manager over the secret store, registration client, and gateway options.</summary>
    public GatewayIdentityManager(
        IGatewaySecretStore secretStore,
        IGatewayRegistrationClient registrationClient,
        IOptions<GatewayOptions> gateway,
        ILogger<GatewayIdentityManager> logger,
        TimeProvider? time = null)
    {
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _registrationClient = registrationClient ?? throw new ArgumentNullException(nameof(registrationClient));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The gateway's configured durable identity.</summary>
    public string GatewayId => _gateway.Value.GatewayId;

    /// <summary>The currently cached identity, or null before the first load/registration.</summary>
    public GatewayIdentity? Current => _cached;

    /// <summary>True when the cached identity is REGISTERED (SP-02); the sync worker gates on this.</summary>
    public bool IsRegistered => _cached is { IsRegistered: true };

    /// <summary>
    /// Loads the persisted identity from the secret store (or returns null on first boot) and caches it.
    /// Does not contact the Relay — this is the cheap, offline-safe read the sync worker uses on every cycle.
    /// </summary>
    public async Task<GatewayIdentity?> LoadAsync(CancellationToken ct = default)
    {
        _cached = await _secretStore.LoadAsync(ct).ConfigureAwait(false);
        if (_cached is not null)
        {
            _logger.LogInformation("Loaded gateway identity for {GatewayId} (state {State})",
                _cached.GatewayId, _cached.State);
        }

        return _cached;
    }

    /// <summary>
    /// Ensures the gateway is REGISTERED with the Relay (SP-02). Offline-first: when no Relay is configured
    /// this returns the local identity as-is (Unregistered) without attempting the network. When a Relay is
    /// configured and the stored identity is PENDING, it resumes by confirming the stored token; when no
    /// token exists it runs the full two-step handshake. Returns the final identity (Registered on success).
    /// </summary>
    /// <exception cref="GatewayRegistrationException">
    /// The Relay permanently rejected the handshake (e.g. suspended/revoked gateway, invalid token).
    /// </exception>
    public async Task<GatewayIdentity> EnsureRegisteredAsync(CancellationToken ct = default)
    {
        var identity = _cached ?? await LoadAsync(ct).ConfigureAwait(false);

        // Offline-first: no Relay configured → the gateway keeps its local identity but cannot sync (SP-01).
        if (!_registrationClient.IsConfigured)
        {
            if (identity is null)
            {
                _logger.LogInformation(
                    "No Relay configured; gateway {GatewayId} stays unregistered (LAN-only, SP-01)",
                    GatewayId);
                identity = new GatewayIdentity { GatewayId = GatewayId, State = GatewayIdentityState.Unregistered };
                _cached = identity;
            }

            return identity;
        }

        if (identity is not null && identity.IsRegistered)
        {
            // Already confirmed by the Relay; nothing to do (the Relay re-checks REGISTERED on every sync).
            return identity;
        }

        if (identity is not null && !string.IsNullOrWhiteSpace(identity.RegistrationToken))
        {
            // PENDING (or state lost but the token survived): resume by confirming the stored token.
            _logger.LogInformation("Gateway {GatewayId} has a stored token; confirming registration", GatewayId);
            return await ConfirmAndPersistAsync(identity, ct).ConfigureAwait(false);
        }

        // First registration: request a token, persist it as PENDING, then confirm.
        _logger.LogInformation("Gateway {GatewayId} is unregistered; requesting a registration token", GatewayId);
        var issued = await _registrationClient
            .RequestRegistrationAsync(GatewayId, _gateway.Value.DisplayName, ct)
            .ConfigureAwait(false);

        var pending = new GatewayIdentity
        {
            GatewayId = GatewayId,
            State = GatewayIdentityState.Pending,
            RegistrationToken = issued.RegistrationToken,
            TokenExpiresAt = issued.TokenExpiresAt,
        };
        await PersistAsync(pending, ct).ConfigureAwait(false);

        return await ConfirmAndPersistAsync(pending, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Rotates the registration token: asks the Relay to issue a fresh one (verifying the current token) and
    /// persists the new token. Used when the current token approaches expiry. Returns the updated identity.
    /// </summary>
    public async Task<GatewayIdentity> RotateTokenAsync(CancellationToken ct = default)
    {
        var identity = _cached ?? await LoadAsync(ct).ConfigureAwait(false);
        if (identity is not { IsRegistered: true } || identity.RegistrationToken is null)
        {
            throw new GatewayRegistrationException(
                $"Gateway '{GatewayId}' is not registered; rotation requires a registered identity.");
        }

        var rotated = await _registrationClient
            .RotateTokenAsync(GatewayId, identity.RegistrationToken, ct)
            .ConfigureAwait(false);

        var updated = identity with
        {
            State = GatewayIdentityState.Registered,
            RegistrationToken = rotated.RegistrationToken,
            TokenExpiresAt = rotated.TokenExpiresAt,
        };
        await PersistAsync(updated, ct).ConfigureAwait(false);
        _logger.LogInformation("Gateway {GatewayId} registration token rotated", GatewayId);
        return updated;
    }

    /// <summary>Presents the stored token to the Relay and persists the REGISTERED state on success.</summary>
    private async Task<GatewayIdentity> ConfirmAndPersistAsync(GatewayIdentity pending, CancellationToken ct)
    {
        var gateway = await _registrationClient
            .ConfirmRegistrationAsync(GatewayId, pending.RegistrationToken!, ct)
            .ConfigureAwait(false);

        if (gateway.Status != GatewayStatus.Registered)
        {
            throw new GatewayRegistrationException(
                $"The Relay did not confirm registration for gateway '{GatewayId}' (status {gateway.Status}).",
                System.Net.HttpStatusCode.Forbidden, code: ErrorCodes.GatewayUnregistered);
        }

        var registered = pending with
        {
            State = GatewayIdentityState.Registered,
            RegisteredAtUtc = _time.GetUtcNow(),
        };
        await PersistAsync(registered, ct).ConfigureAwait(false);
        _logger.LogInformation("Gateway {GatewayId} registered with the Relay", GatewayId);
        return registered;
    }

    private async Task PersistAsync(GatewayIdentity identity, CancellationToken ct)
    {
        await _secretStore.SaveAsync(identity, ct).ConfigureAwait(false);
        _cached = identity;
    }
}
