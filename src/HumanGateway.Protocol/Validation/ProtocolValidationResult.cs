namespace HumanGateway.Protocol.Validation;

/// <summary>
/// The result of validating a protocol entity: valid, or a list of structured
/// <see cref="ProtocolValidationError"/>s. The aggregate error code for protocol violations is
/// <see cref="ValidationErrorCodes.ValidationFailed"/> (the reserved protocol error catalog).
/// </summary>
public sealed class ProtocolValidationResult
{
    /// <summary>A singleton representing a valid result.</summary>
    public static readonly ProtocolValidationResult Valid = new(Array.Empty<ProtocolValidationError>());

    /// <summary>The structured validation errors; empty when <see cref="IsValid"/> is true.</summary>
    public IReadOnlyList<ProtocolValidationError> Errors { get; }

    /// <summary>True when the entity satisfies the protocol schema rules.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>Creates a result from the given errors.</summary>
    public ProtocolValidationResult(IReadOnlyList<ProtocolValidationError> errors)
    {
        Errors = errors;
    }

    /// <summary>Builds an invalid result from one or more errors.</summary>
    public static ProtocolValidationResult Invalid(params ProtocolValidationError[] errors) => new(errors);

    /// <summary>Throws <see cref="ProtocolValidationException"/> when the result is invalid.</summary>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new ProtocolValidationException(this);
        }
    }
}
