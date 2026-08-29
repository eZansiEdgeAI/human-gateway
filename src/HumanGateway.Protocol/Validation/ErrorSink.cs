namespace HumanGateway.Protocol.Validation;

/// <summary>
/// Internal error collector used by the entity validators so nested validation (e.g. a recipient inside a
/// message) accumulates all errors in one pass with prefixed JSON paths.
/// </summary>
internal sealed class ErrorSink
{
    private readonly List<ProtocolValidationError> _errors = new();

    public bool HasErrors => _errors.Count > 0;

    public void Add(string code, string path, string message)
        => _errors.Add(new ProtocolValidationError(code, path, message));

    /// <summary>Adds an error unless <paramref name="condition"/> is true.</summary>
    public void Require(bool condition, string code, string path, string message)
    {
        if (!condition)
        {
            Add(code, path, message);
        }
    }

    public ProtocolValidationResult ToResult()
        => HasErrors ? new ProtocolValidationResult(_errors) : ProtocolValidationResult.Valid;
}
