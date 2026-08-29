namespace HumanGateway.Protocol.Validation;

/// <summary>
/// Thrown by <see cref="ProtocolValidationResult.ThrowIfInvalid"/> when a protocol entity fails validation.
/// Carries the structured <see cref="ProtocolValidationResult"/> for programmatic handling.
/// </summary>
public sealed class ProtocolValidationException : Exception
{
    /// <summary>The structured validation result that caused this exception.</summary>
    public ProtocolValidationResult Result { get; }

    /// <summary>Creates the exception from an invalid result.</summary>
    public ProtocolValidationException(ProtocolValidationResult result)
        : base($"Protocol validation failed with {result.Errors.Count} error(s): {string.Join("; ", result.Errors)}")
    {
        Result = result;
    }
}
