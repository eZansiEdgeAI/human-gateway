using System.Text.RegularExpressions;
using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Services;
using HumanGateway.Relay.Storage.Entities;

namespace HumanGateway.Relay.Api;

/// <summary>
/// Wire models for the gateway registration + rendezvous API (RELAY-FR-03, WEBX-FR-02). Request records are
/// validated against the gateway/participant schema shapes before any store access; responses follow the
/// Relay JSON contract (camelCase, exact enum tokens, omit-null).
/// </summary>
public static partial class RequestValidation
{
    /// <summary>common.schema.json#/$defs/id — durable ID, 8..128 chars.</summary>
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{7,127}$")]
    public static partial Regex GatewayIdRegex();

    /// <summary>gateway.schema.json#/$defs/registrationToken — <c>hgrt_</c> + base64url body (48..256 chars).</summary>
    [GeneratedRegex("^hgrt_[A-Za-z0-9_-]{43,251}$")]
    public static partial Regex RegistrationTokenRegex();

    /// <summary>common.schema.json#/$defs/participantAddress — (human|agent|system):suffix (PROTO-FR-02).</summary>
    [GeneratedRegex("^(human|agent|system):[A-Za-z0-9._@+-]{1,191}$")]
    public static partial Regex ParticipantAddressRegex();

    /// <summary>Rejects a request with a VALIDATION_FAILED 400 when the message is non-empty.</summary>
    internal static void ThrowIfInvalid(List<string> errors)
    {
        if (errors.Count > 0)
        {
            throw GatewayServiceException.BadRequest(ErrorCodes.ValidationFailed, string.Join("; ", errors));
        }
    }
}

/// <summary>Body of <c>POST /gateways</c> — the initial registration handshake (AUTH-FR-01).</summary>
public sealed record RegisterGatewayRequest
{
    /// <summary>The Edge's proposed durable gateway identity (common.schema.json#/$defs/id).</summary>
    public string GatewayId { get; init; } = null!;

    /// <summary>Optional human-readable gateway name (e.g. the school or site name).</summary>
    public string? DisplayName { get; init; }

    /// <summary>Validates the request against gateway.schema.json shapes; throws a 400 on failure.</summary>
    public void Validate()
    {
        var errors = new List<string>();
        if (!RequestValidation.GatewayIdRegex().IsMatch(GatewayId ?? string.Empty))
        {
            errors.Add("gatewayId must be a durable ID (8..128 chars of [A-Za-z0-9._:-]); got "
                + (GatewayId is null ? "null" : $"'{GatewayId}'") + ".");
        }

        if (DisplayName is not null && DisplayName is { Length: < 1 or > 255 })
        {
            errors.Add("displayName must be 1..255 characters.");
        }

        RequestValidation.ThrowIfInvalid(errors);
    }
}

/// <summary>Body of <c>POST /gateways/{gatewayId}/register</c> and <c>/rotate</c> — presents the registration token.</summary>
public sealed record ConfirmRegistrationRequest
{
    /// <summary>The gateway confirming or rotating its token.</summary>
    public string GatewayId { get; init; } = null!;

    /// <summary>The registration token (gateway.schema.json <c>$defs.registrationToken</c>). Never logged (SP-07).</summary>
    public string RegistrationToken { get; init; } = null!;

    /// <summary>Validates the request against gateway.schema.json shapes; throws a 400 on failure.</summary>
    public void Validate()
    {
        var errors = new List<string>();
        if (!RequestValidation.GatewayIdRegex().IsMatch(GatewayId ?? string.Empty))
        {
            errors.Add("gatewayId must be a durable ID (8..128 chars of [A-Za-z0-9._:-]); got "
                + (GatewayId is null ? "null" : $"'{GatewayId}'") + ".");
        }

        if (!RequestValidation.RegistrationTokenRegex().IsMatch(RegistrationToken ?? string.Empty))
        {
            errors.Add("registrationToken must be an 'hgrt_' token (48..256 chars of [A-Za-z0-9_-]).");
        }

        RequestValidation.ThrowIfInvalid(errors);
    }
}

/// <summary>
/// Response of <c>POST /gateways</c> and <c>/gateways/{gatewayId}/rotate</c>. The plaintext
/// <see cref="RegistrationToken"/> is returned exactly once, over TLS; the Relay persists only its
/// fingerprint (SP-07). The caller must store it in the Edge secret store before the response is discarded.
/// </summary>
public sealed record RegistrationIssued
{
    public string GatewayId { get; init; } = null!;

    /// <summary>Gateway registration lifecycle after this call (PENDING, or REGISTERED after a rotation).</summary>
    public string Status { get; init; } = null!;

    /// <summary>The one-time plaintext registration token. Handle as a secret.</summary>
    public string RegistrationToken { get; init; } = null!;

    public string TokenIssuedAt { get; init; } = null!;

    public string TokenExpiresAt { get; init; } = null!;

    /// <summary>Builds the issued-token response from the durable record and the plaintext token.</summary>
    public static RegistrationIssued From(GatewayRecord record, string plaintextToken) => new()
    {
        GatewayId = record.GatewayId,
        Status = record.Status ?? "PENDING",
        RegistrationToken = plaintextToken,
        TokenIssuedAt = record.TokenIssuedAt ?? string.Empty,
        TokenExpiresAt = record.TokenExpiresAt ?? string.Empty,
    };
}

/// <summary>Rendezvous summary of a registered gateway (WEBX-FR-02).</summary>
public sealed record RendezvousGatewayInfo
{
    public string GatewayId { get; init; } = null!;
    public string? DisplayName { get; init; }

    /// <summary>Always REGISTERED — only registered gateways are rendezvous targets (SP-02).</summary>
    public string Status { get; init; } = null!;

    public string? LastSeenAt { get; init; }

    /// <summary>True when the gateway synced within the rendezvous online window (default 15 min).</summary>
    public bool Online { get; init; }
}

/// <summary>Rendezvous resolution of a participant address to its serving school Edge (WEBX-FR-02).</summary>
public sealed record RendezvousLookup
{
    public string ParticipantAddress { get; init; } = null!;
    public string GatewayId { get; init; } = null!;
    public string? GatewayDisplayName { get; init; }
    public string? LastSeenAt { get; init; }
    public bool Online { get; init; }
}
