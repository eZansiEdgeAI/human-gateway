using HumanGateway.Protocol.Models;

namespace HumanGateway.Protocol.Validation;

/// <summary>
/// Validates a <see cref="User"/> against user.schema.json (AUTH-FR-02, SP-03, SP-07): local Edge account
/// shape — login username, display name, PHC password verifier (local-store only; never transmitted),
/// status, and per-status timestamp invariants.
/// </summary>
public sealed class UserValidator : IProtocolValidator<User>
{
    internal static readonly UserValidator Instance = new();

    /// <inheritdoc />
    public ProtocolValidationResult Validate(User? value)
    {
        if (value is null)
        {
            return ProtocolValidationResult.Invalid(
                new ProtocolValidationError(ValidationErrorCodes.Required, "#", "User is required (user.schema.json)."));
        }

        var sink = new ErrorSink();
        ValidateInto(value, "", sink);
        return sink.ToResult();
    }

    internal void ValidateInto(User? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "User is required.");
            return;
        }

        CommonRules.Id(value.Id, $"{path}id", sink);
        // username and passwordVerifier are schema-required (user.schema.json "required"); unlike an
        // optional pattern check, a missing value is a REQUIRED error, not a pass.
        CommonRules.RequiredPattern(value.Username, $"{path}username", sink, CommonRules.UsernameRegex(), "username");
        CommonRules.Text(value.DisplayName, $"{path}displayName", sink, true, 1, 255, "displayName");

        // passwordVerifier is a local-store-only field; the schema requires its PHC shape.
        CommonRules.RequiredPattern(value.PasswordVerifier, $"{path}passwordVerifier", sink, CommonRules.PasswordVerifierRegex(),
            "passwordVerifier (PHC string)");

        UserStatus? status = null;
        if (value.Status is not { } s)
        {
            sink.Add(ValidationErrorCodes.Required, $"{path}status", "User status is required (ACTIVE|DISABLED).");
        }
        else if (!Enum.IsDefined(s))
        {
            sink.Add(ValidationErrorCodes.UndefinedEnum, $"{path}status", $"'{s}' is not a defined user status.");
        }
        else
        {
            status = s;
        }

        if (!Enum.IsDefined(value.Role))
        {
            sink.Add(ValidationErrorCodes.UndefinedEnum, $"{path}role", $"'{value.Role}' is not a defined user role.");
        }

        if (value.LastLoginAt is not null)
        {
            CommonRules.Timestamp(value.LastLoginAt, $"{path}lastLoginAt", sink);
        }
        if (value.DisabledAt is not null)
        {
            CommonRules.Timestamp(value.DisabledAt, $"{path}disabledAt", sink);
        }

        CommonRules.Timestamp(value.CreatedAt, $"{path}createdAt", sink);
        if (value.UpdatedAt is not null)
        {
            CommonRules.Timestamp(value.UpdatedAt, $"{path}updatedAt", sink);
        }

        // Snapshot invariant (user.schema.json allOf).
        if (status == UserStatus.Disabled)
        {
            sink.Require(value.DisabledAt is not null, ValidationErrorCodes.StateTimestampRequired, $"{path}disabledAt",
                "DISABLED users must record disabledAt.");
        }
    }
}
