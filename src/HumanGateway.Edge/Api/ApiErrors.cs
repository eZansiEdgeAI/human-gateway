using HumanGateway.Core.Artifacts;
using HumanGateway.Protocol.Models;
using HumanGateway.Protocol.Validation;
using HumanGateway.Security;
using Microsoft.EntityFrameworkCore;

namespace HumanGateway.Edge.Api;

/// <summary>
/// Builds consistent <see cref="ProtocolError"/>-shaped HTTP error responses for the local API. Error codes use
/// the reserved protocol catalog (<see cref="ErrorCodes"/>); messages are human-safe (SP-07).
/// </summary>
public static class ApiErrors
{
    /// <summary>400 — a request body/query is malformed or fails protocol validation.</summary>
    public static IResult ValidationFailed(IReadOnlyList<ProtocolValidationError> errors)
    {
        var message = $"Validation failed: {string.Join("; ", errors.Take(5))}";
        var details = errors.Select(e => new
        {
            code = e.Code,
            path = e.Path,
            message = e.Message,
        }).ToArray();

        return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, message, details, retryable: false);
    }

    /// <summary>400 — a domain rule was violated (e.g. approval without a decision).</summary>
    public static IResult BadRequest(string code, string message)
        => Problem(StatusCodes.Status400BadRequest, code, message, null, retryable: false);

    /// <summary>404 — the requested resource does not exist.</summary>
    public static IResult NotFound(string message)
        => Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, message, null, retryable: false);

    /// <summary>409 — the request conflicts with an existing durable record.</summary>
    public static IResult Conflict(string message)
        => Problem(StatusCodes.Status409Conflict, ErrorCodes.Conflict, message, null, retryable: false);

    /// <summary>500 — an unexpected internal failure (message kept generic for SP-07).</summary>
    public static IResult InternalError()
        => Problem(StatusCodes.Status500InternalServerError, ErrorCodes.InternalError, "An internal error occurred.", null, retryable: true);

    /// <summary>Maps a <see cref="LocalApiException"/> to its HTTP response.</summary>
    public static IResult FromLocalApiException(LocalApiException ex)
        => Problem(ex.StatusCode, ex.Code, ex.Message, null, ex.StatusCode >= 500);

    /// <summary>Maps an arbitrary exception to a response; the default is a generic 500 (SP-07).</summary>
    public static IResult FromException(Exception ex) => ex switch
    {
        ProtocolValidationException ve => ValidationFailed(ve.Result.Errors),
        LocalApiException le => FromLocalApiException(le),
        UnauthenticatedRequestException => Problem(
            StatusCodes.Status401Unauthorized,
            ErrorCodes.Unauthorized,
            "A valid session is required to access this resource.",
            null,
            retryable: false),
        ForbiddenRequestException => Problem(
            StatusCodes.Status403Forbidden,
            ErrorCodes.Forbidden,
            "Administrator access is required for this endpoint.",
            null,
            retryable: false),
        ArtifactHashMismatchException e => Problem(
            StatusCodes.Status422UnprocessableEntity,
            ErrorCodes.HashMismatch,
            e.Message,
            new { declaredHash = e.DeclaredHash, actualHash = e.ActualHash },
            retryable: false),
        DbUpdateException => Conflict("The request conflicts with an existing durable record."),
        OperationCanceledException => throw ex,
        _ => InternalError(),
    };

    private static IResult Problem(int statusCode, string code, string message, object? details, bool retryable)
    {
        var error = new ProtocolError
        {
            Code = code,
            Message = message,
            Retryable = retryable,
            Details = details is null ? null : System.Text.Json.JsonSerializer.SerializeToElement(details),
        };
        return Results.Json(error, statusCode: statusCode);
    }
}

/// <summary>
/// A domain-rule violation raised by the local API service, carrying an HTTP status code and a reserved
/// protocol error code. The endpoint layer maps it to a <see cref="ProtocolError"/>-shaped response.
/// </summary>
public sealed class LocalApiException : Exception
{
    /// <summary>The HTTP status code to return.</summary>
    public int StatusCode { get; }

    /// <summary>The stable protocol error code (see <see cref="ErrorCodes"/>).</summary>
    public string Code { get; }

    /// <summary>Creates the exception.</summary>
    public LocalApiException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}
