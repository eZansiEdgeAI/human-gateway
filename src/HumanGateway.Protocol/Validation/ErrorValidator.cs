using HumanGateway.Protocol.Models;

namespace HumanGateway.Protocol.Validation;

/// <summary>
/// Validates a <see cref="ProtocolError"/> against error.schema.json (protocol §7 #3): required code +
/// message, UPPER_SNAKE code shape (reserved catalog + extensions), bounds, and secret hygiene is a
/// content policy enforced by callers (SP-07).
/// </summary>
public sealed class ErrorValidator : IProtocolValidator<ProtocolError>
{
    internal static readonly ErrorValidator Instance = new();

    /// <inheritdoc />
    public ProtocolValidationResult Validate(ProtocolError? value)
    {
        if (value is null)
        {
            return ProtocolValidationResult.Invalid(
                new ProtocolValidationError(ValidationErrorCodes.Required, "#", "Error is required (error.schema.json)."));
        }

        var sink = new ErrorSink();
        ValidateInto(value, "", sink);
        return sink.ToResult();
    }

    internal void ValidateInto(ProtocolError? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "Error is required (error.schema.json).");
            return;
        }

        // code — UPPER_SNAKE token, 1..64 chars; the reserved catalog may be extended with the same shape.
        if (value.Code is null)
        {
            sink.Add(ValidationErrorCodes.Required, $"{path}code", "Error code is required.");
        }
        else if (value.Code.Length is < 1 or > 64 || !CommonRules.ErrorCodeRegex().IsMatch(value.Code))
        {
            sink.Add(ValidationErrorCodes.InvalidPattern, $"{path}code",
                "Error code must be an UPPER_SNAKE token of 1..64 chars (error.schema.json#/code).");
        }

        CommonRules.Text(value.Message, $"{path}message", sink, true, 1, 1024, "Error message");
        CommonRules.JsonObject(value.Details, $"{path}details", sink, "details");
        // retryable is optional; the bool? type guarantees boolean semantics at the wire boundary.
    }
}
