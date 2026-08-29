using HumanGateway.Protocol.Models;

namespace HumanGateway.Protocol.Validation;

/// <summary>
/// Validates a <see cref="Gateway"/> against gateway.schema.json (AUTH-FR-01, SP-02, SP-07): gateway
/// identity, registration lifecycle, token fingerprint shape, and per-status timestamp invariants.
/// </summary>
public sealed class GatewayValidator : IProtocolValidator<Gateway>
{
    internal static readonly GatewayValidator Instance = new();

    /// <inheritdoc />
    public ProtocolValidationResult Validate(Gateway? value)
    {
        if (value is null)
        {
            return ProtocolValidationResult.Invalid(
                new ProtocolValidationError(ValidationErrorCodes.Required, "#", "Gateway is required (gateway.schema.json)."));
        }

        var sink = new ErrorSink();
        ValidateInto(value, "", sink);
        return sink.ToResult();
    }

    internal void ValidateInto(Gateway? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "Gateway is required.");
            return;
        }

        CommonRules.Id(value.GatewayId, $"{path}gatewayId", sink);
        CommonRules.Text(value.DisplayName, $"{path}displayName", sink, false, 1, 255, "displayName");

        GatewayStatus? status = null;
        if (value.Status is not { } s)
        {
            sink.Add(ValidationErrorCodes.Required, $"{path}status", "Gateway status is required (UNREGISTERED|PENDING|REGISTERED|SUSPENDED|REVOKED).");
        }
        else if (!Enum.IsDefined(s))
        {
            sink.Add(ValidationErrorCodes.UndefinedEnum, $"{path}status", $"'{s}' is not a defined gateway status.");
        }
        else
        {
            status = s;
        }

        if (value.RegistrationTokenFingerprint is not null)
        {
            CommonRules.Pattern(value.RegistrationTokenFingerprint, $"{path}registrationTokenFingerprint", sink,
                CommonRules.Sha256FingerprintRegex(), "registrationTokenFingerprint");
        }

        if (value.TokenIssuedAt is not null)
        {
            CommonRules.Timestamp(value.TokenIssuedAt, $"{path}tokenIssuedAt", sink);
        }
        if (value.TokenExpiresAt is not null)
        {
            CommonRules.Timestamp(value.TokenExpiresAt, $"{path}tokenExpiresAt", sink);
        }
        if (value.RegisteredAt is not null)
        {
            CommonRules.Timestamp(value.RegisteredAt, $"{path}registeredAt", sink);
        }
        if (value.SuspendedAt is not null)
        {
            CommonRules.Timestamp(value.SuspendedAt, $"{path}suspendedAt", sink);
        }
        if (value.RevokedAt is not null)
        {
            CommonRules.Timestamp(value.RevokedAt, $"{path}revokedAt", sink);
        }
        if (value.LastSeenAt is not null)
        {
            CommonRules.Timestamp(value.LastSeenAt, $"{path}lastSeenAt", sink);
        }

        CommonRules.Timestamp(value.CreatedAt, $"{path}createdAt", sink);
        if (value.UpdatedAt is not null)
        {
            CommonRules.Timestamp(value.UpdatedAt, $"{path}updatedAt", sink);
        }

        // Snapshot invariants (gateway.schema.json allOf).
        if (status == GatewayStatus.Suspended)
        {
            sink.Require(value.SuspendedAt is not null, ValidationErrorCodes.StateTimestampRequired, $"{path}suspendedAt",
                "SUSPENDED gateways must record suspendedAt.");
        }
        if (status == GatewayStatus.Revoked)
        {
            sink.Require(value.RevokedAt is not null, ValidationErrorCodes.StateTimestampRequired, $"{path}revokedAt",
                "REVOKED gateways must record revokedAt.");
        }
    }
}
