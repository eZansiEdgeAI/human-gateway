using HumanGateway.Core.Time;
using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Api;
using HumanGateway.Relay.Options;
using HumanGateway.Relay.Security;
using HumanGateway.Relay.Storage;
using HumanGateway.Relay.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HumanGateway.Relay.Services;

/// <summary>
/// Gateway registration lifecycle (RELAY-FR-03, AUTH-FR-01, SP-02, SP-07): request a registration token,
/// confirm registration by presenting it, and rotate the token before it expires. The Relay persists only the
/// SHA-256 token fingerprint — the plaintext token is returned to the caller exactly once (SP-07) and is
/// never logged. Only REGISTERED gateways are accepted as rendezvous/sync targets; SUSPENDED and REVOKED
/// identities are rejected on every operation (SP-02).
/// </summary>
public sealed class GatewayService
{
    private readonly IDbContextFactory<RelayDbContext> _factory;
    private readonly RelayOptions _options;
    private readonly ILogger<GatewayService> _logger;

    public GatewayService(
        IDbContextFactory<RelayDbContext> factory,
        IOptions<RelayOptions> options,
        ILogger<GatewayService> logger)
    {
        _factory = factory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Step 1 of the registration handshake: creates the gateway identity in PENDING and issues a fresh
    /// registration token (returned once). Idempotency note: an existing PENDING/UNREGISTERED identity is a
    /// 409 — the previously issued token is unrecoverable (only its fingerprint is stored), so a re-request
    /// must not silently invalidate it (SP-07). Use the issued token, or rotate once registered.
    /// </summary>
    public async Task<RegistrationIssued> RequestRegistrationAsync(RegisterGatewayRequest request, CancellationToken ct)
    {
        request.Validate();
        await using var db = await _factory.CreateDbContextAsync(ct);

        var existing = await db.Gateways.FirstOrDefaultAsync(g => g.GatewayId == request.GatewayId, ct);
        if (existing is not null)
        {
            throw existing.Status switch
            {
                "SUSPENDED" => GatewayServiceException.Forbidden(ErrorCodes.GatewaySuspended,
                    "The gateway is suspended and cannot be re-registered (SP-02)."),
                "REVOKED" => GatewayServiceException.Forbidden(ErrorCodes.GatewayRevoked,
                    "The gateway identity is revoked and cannot be re-registered (SP-02)."),
                "REGISTERED" => GatewayServiceException.Conflict(
                    $"Gateway '{request.GatewayId}' is already registered; use /gateways/{request.GatewayId}/rotate to change its token."),
                _ => GatewayServiceException.Conflict(
                    $"Gateway '{request.GatewayId}' already has a registration in progress; use the previously issued token, or rotate once registered."),
            };
        }

        var token = RegistrationTokens.Generate();
        var now = ProtocolTime.Now();
        var expiresAt = ProtocolTime.Format(DateTimeOffset.UtcNow.AddDays(_options.RegistrationTokenTtlDays));

        var record = new GatewayRecord
        {
            GatewayId = request.GatewayId,
            DisplayName = request.DisplayName,
            Status = RelayJsonConversions.WireToken(GatewayStatus.Pending),
            RegistrationTokenFingerprint = RegistrationTokens.Fingerprint(token),
            TokenIssuedAt = now,
            TokenExpiresAt = expiresAt,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Gateways.Add(record);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Gateway registration requested: {GatewayId} (PENDING, token TTL {TtlDays}d)",
            request.GatewayId, _options.RegistrationTokenTtlDays);
        return RegistrationIssued.From(record, token);
    }

    /// <summary>
    /// Step 2 of the registration handshake: presents the issued token and moves the identity to REGISTERED.
    /// Rejects unregistered, suspended, and revoked identities (SP-02) and any token mismatch or expiry
    /// (REGISTRATION_TOKEN_INVALID / REGISTRATION_TOKEN_EXPIRED).
    /// </summary>
    public async Task<GatewayRecord> ConfirmRegistrationAsync(ConfirmRegistrationRequest request, CancellationToken ct)
    {
        request.Validate();
        await using var db = await _factory.CreateDbContextAsync(ct);

        var record = await db.Gateways.FirstOrDefaultAsync(g => g.GatewayId == request.GatewayId, ct)
            ?? throw GatewayServiceException.NotFound($"Gateway '{request.GatewayId}' is not registered (SP-02).");

        EnsureStatus(record, GatewayStatus.Pending, "confirm registration");
        VerifyTokenOrThrow(record, request.RegistrationToken);

        var now = ProtocolTime.Now();
        record.Status = RelayJsonConversions.WireToken(GatewayStatus.Registered);
        record.RegisteredAt ??= now;
        record.LastSeenAt = now;
        record.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Gateway registered: {GatewayId}", request.GatewayId);
        return record;
    }

    /// <summary>
    /// Rotates the registration token of a REGISTERED gateway: verifies the current token, issues a fresh one,
    /// and persists only its fingerprint. The new token replaces the old immediately (the old token stops
    /// working) — the caller must persist the returned plaintext token in the Edge secret store.
    /// </summary>
    public async Task<RegistrationIssued> RotateTokenAsync(ConfirmRegistrationRequest request, CancellationToken ct)
    {
        request.Validate();
        await using var db = await _factory.CreateDbContextAsync(ct);

        var record = await db.Gateways.FirstOrDefaultAsync(g => g.GatewayId == request.GatewayId, ct)
            ?? throw GatewayServiceException.NotFound($"Gateway '{request.GatewayId}' is not registered (SP-02).");

        EnsureStatus(record, GatewayStatus.Registered, "rotate its token");
        VerifyTokenOrThrow(record, request.RegistrationToken);

        var token = RegistrationTokens.Generate();
        var now = ProtocolTime.Now();
        record.RegistrationTokenFingerprint = RegistrationTokens.Fingerprint(token);
        record.TokenIssuedAt = now;
        record.TokenExpiresAt = ProtocolTime.Format(DateTimeOffset.UtcNow.AddDays(_options.RegistrationTokenTtlDays));
        record.LastSeenAt = now;
        record.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Gateway registration token rotated: {GatewayId}", request.GatewayId);
        return RegistrationIssued.From(record, token);
    }

    /// <summary>
    /// Loads a gateway that must be in the REGISTERED state (SP-02). Used by rendezvous routing and the sync
    /// endpoint: unregistered, pending, suspended, and revoked identities are rejected.
    /// </summary>
    public async Task<GatewayRecord> RequireRegisteredAsync(string gatewayId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = await db.Gateways.FirstOrDefaultAsync(g => g.GatewayId == gatewayId, ct)
            ?? throw GatewayServiceException.NotFound($"Gateway '{gatewayId}' is not registered (SP-02).");
        EnsureStatus(record, GatewayStatus.Registered, "perform this operation");
        return record;
    }

    /// <summary>
    /// Non-throwing registered check (SP-02): true only for identities currently in the REGISTERED state.
    /// Used by cross-school routing to decide whether a message recipient's serving gateway is a valid
    /// delivery target — PENDING/SUSPENDED/REVOKED gateways are never routed to.
    /// </summary>
    public async Task<bool> IsRegisteredAsync(string gatewayId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(gatewayId))
        {
            return false;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = await db.Gateways.AsNoTracking().FirstOrDefaultAsync(g => g.GatewayId == gatewayId, ct);
        return record?.Status == "REGISTERED";
    }

    /// <summary>
    /// Records gateway sync activity: refreshes <c>lastSeenAt</c> (the rendezvous "online" watermark) and
    /// <c>updatedAt</c> on every successful sync exchange. No-op for unknown identities — the sync endpoints
    /// have already rejected them via <see cref="RequireRegisteredAsync"/>.
    /// </summary>
    public async Task TouchLastSeenAsync(string gatewayId, DateTimeOffset atUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = await db.Gateways.FirstOrDefaultAsync(g => g.GatewayId == gatewayId, ct);
        if (record is null)
        {
            return;
        }

        record.LastSeenAt = ProtocolTime.Format(atUtc);
        record.UpdatedAt = ProtocolTime.Format(atUtc);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Ensures the identity is in <paramref name="required"/> state for the given operation, mapping every
    /// other state to the SP-02 rejection codes (GATEWAY_SUSPENDED / GATEWAY_REVOKED / GATEWAY_UNREGISTERED).
    /// </summary>
    private static void EnsureStatus(GatewayRecord record, GatewayStatus required, string operation)
    {
        switch (record.Status)
        {
            case "SUSPENDED":
                throw GatewayServiceException.Forbidden(ErrorCodes.GatewaySuspended,
                    $"The gateway '{record.GatewayId}' is suspended and cannot {operation} (SP-02).");
            case "REVOKED":
                throw GatewayServiceException.Forbidden(ErrorCodes.GatewayRevoked,
                    $"The gateway '{record.GatewayId}' is revoked and cannot {operation} (SP-02).");
            case "REGISTERED" when required != GatewayStatus.Registered:
                throw GatewayServiceException.Conflict(
                    $"Gateway '{record.GatewayId}' is already registered; use /gateways/{record.GatewayId}/rotate to change its token.");
            case "PENDING" when required != GatewayStatus.Pending:
                throw GatewayServiceException.Forbidden(ErrorCodes.GatewayUnregistered,
                    $"Gateway '{record.GatewayId}' has not completed registration and cannot {operation} (SP-02).");
            case null or "UNREGISTERED":
                throw GatewayServiceException.Forbidden(ErrorCodes.GatewayUnregistered,
                    $"Gateway '{record.GatewayId}' is not registered and cannot {operation} (SP-02).");
        }
    }

    /// <summary>
    /// Verifies a presented token against the stored fingerprint (constant-time) and its expiry. Token bytes
    /// never reach the logger or any error payload (SP-07).
    /// </summary>
    private static void VerifyTokenOrThrow(GatewayRecord record, string? presentedToken)
    {
        if (!RegistrationTokens.IsWellFormed(presentedToken)
            || !RegistrationTokens.Verify(presentedToken, record.RegistrationTokenFingerprint))
        {
            throw GatewayServiceException.Forbidden(ErrorCodes.RegistrationTokenInvalid,
                "The registration token is invalid (SP-07).");
        }

        if (record.TokenExpiresAt is { } expiry
            && ProtocolTime.TryParse(expiry, out var expiresAt)
            && expiresAt <= DateTimeOffset.UtcNow)
        {
            throw GatewayServiceException.Forbidden(ErrorCodes.RegistrationTokenExpired,
                "The registration token has expired; request a new one via /gateways/{gatewayId}/rotate.");
        }
    }
}

/// <summary>Maps a durable <see cref="GatewayRecord"/> to its protocol <see cref="Gateway"/> wire form.</summary>
public static class GatewayRecordMapping
{
    /// <summary>Projects the stored record to the canonical gateway.schema.json shape (status wire token).</summary>
    public static Gateway ToProtocol(this GatewayRecord record) => new()
    {
        GatewayId = record.GatewayId,
        DisplayName = record.DisplayName,
        Status = record.Status is { } status && Enum.TryParse<GatewayStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : null,
        RegistrationTokenFingerprint = record.RegistrationTokenFingerprint,
        TokenIssuedAt = record.TokenIssuedAt,
        TokenExpiresAt = record.TokenExpiresAt,
        RegisteredAt = record.RegisteredAt,
        SuspendedAt = record.SuspendedAt,
        RevokedAt = record.RevokedAt,
        LastSeenAt = record.LastSeenAt,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt,
    };
}
