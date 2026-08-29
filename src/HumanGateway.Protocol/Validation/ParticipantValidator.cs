using HumanGateway.Protocol.Models;

namespace HumanGateway.Protocol.Validation;

/// <summary>
/// Validates a <see cref="Participant"/> against participant.schema.json (PROTO-FR-02): typed address,
/// kind/address agreement (anyOf), display metadata, and identity links (userId / gatewayId).
/// </summary>
public sealed class ParticipantValidator : IProtocolValidator<Participant>
{
    internal static readonly ParticipantValidator Instance = new();

    /// <inheritdoc />
    public ProtocolValidationResult Validate(Participant? value)
    {
        if (value is null)
        {
            return ProtocolValidationResult.Invalid(
                new ProtocolValidationError(ValidationErrorCodes.Required, "#", "Participant is required (participant.schema.json)."));
        }

        var sink = new ErrorSink();
        ValidateInto(value, "", sink);
        return sink.ToResult();
    }

    internal void ValidateInto(Participant? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "Participant is required.");
            return;
        }

        CommonRules.ParticipantAddress(value.Address, $"{path}address", sink);

        if (value.Kind is not { } kind)
        {
            sink.Add(ValidationErrorCodes.Required, $"{path}kind", "Participant kind is required (human|agent|system).");
        }
        else if (!Enum.IsDefined(kind))
        {
            sink.Add(ValidationErrorCodes.UndefinedEnum, $"{path}kind", $"'{kind}' is not a defined participant kind.");
        }

        CommonRules.Text(value.DisplayName, $"{path}displayName", sink, true, 1, 255, "displayName");

        if (value.UserId is not null)
        {
            CommonRules.Text(value.UserId, $"{path}userId", sink, false, 1, 128, "userId");
        }

        if (value.GatewayId is not null)
        {
            CommonRules.Id(value.GatewayId, $"{path}gatewayId", sink);
        }

        // anyOf: the address prefix must agree with the kind.
        if (value.Kind is { } k && Enum.IsDefined(k) && value.Address is not null)
        {
            var prefix = k switch
            {
                ParticipantKind.Human => "human:",
                ParticipantKind.Agent => "agent:",
                _ => "system:",
            };
            if (!value.Address.StartsWith(prefix, StringComparison.Ordinal))
            {
                sink.Add(ValidationErrorCodes.AddressKindMismatch, $"{path}address",
                    $"Address prefix must match kind '{prefix.TrimEnd(':')}' (participant.schema.json anyOf, PROTO-FR-02).");
            }
        }
    }
}
