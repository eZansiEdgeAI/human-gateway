namespace HumanGateway.Protocol.Validation;

/// <summary>
/// Validates an entity of type <typeparamref name="T"/> against the protocol schema rules
/// (hand-written validators mirroring the versioned schemas under <c>schemas/</c>; the schemas remain the
/// single source of truth, NF-06).
/// </summary>
public interface IProtocolValidator<in T>
{
    /// <summary>Validates <paramref name="value"/> and returns a structured result.</summary>
    ProtocolValidationResult Validate(T? value);
}
