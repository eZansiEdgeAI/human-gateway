using HumanGateway.Core.Artifacts;
using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Services;
using HumanGateway.Security;
using Microsoft.EntityFrameworkCore;

namespace HumanGateway.Relay.Api;

/// <summary>
/// Builds consistent <see cref="ProtocolError"/>-shaped HTTP error responses for the Relay API. Error codes use
/// the reserved protocol catalog (<see cref="ErrorCodes"/>); messages are human-safe (SP-07).
/// </summary>
public static class ApiErrors
{
    /// <summary>400 — a request body/query is malformed.</summary>
    public static IResult BadRequest(string code, string message)
        => Problem(StatusCodes.Status400BadRequest, code, message, null, retryable: false);

    /// <summary>401/403 — the gateway failed identity/authorisation (SP-02, SP-07).</summary>
    public static IResult Forbidden(string code, string message)
        => Problem(StatusCodes.Status403Forbidden, code, message, null, retryable: false);

    /// <summary>404 — the requested resource does not exist.</summary>
    public static IResult NotFound(string message)
        => Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, message, null, retryable: false);

    /// <summary>409 — the request conflicts with an existing durable record.</summary>
    public static IResult Conflict(string message)
        => Problem(StatusCodes.Status409Conflict, ErrorCodes.Conflict, message, null, retryable: false);

    /// <summary>500 — an unexpected internal failure (message kept generic for SP-07).</summary>
    public static IResult InternalError()
        => Problem(StatusCodes.Status500InternalServerError, ErrorCodes.InternalError, "An internal error occurred.", null, retryable: true);

    /// <summary>Maps an arbitrary exception to a response; the default is a generic 500 (SP-07).</summary>
    public static IResult FromException(Exception ex) => ex switch
    {
        GatewayServiceException e => Problem(e.StatusCode, e.Code, e.Message, null, e.Retryable),
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
