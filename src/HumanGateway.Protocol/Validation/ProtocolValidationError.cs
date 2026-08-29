namespace HumanGateway.Protocol.Validation;

/// <summary>
/// A single structured validation error: the offending JSON path, a stable machine-readable code, and a
/// human-readable message. Paths follow JSON Pointer-ish dot notation (e.g. <c>recipients[0].address</c>).
/// </summary>
public sealed record ProtocolValidationError(string Code, string Path, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"{Path}: {Message} [{Code}]";
}
