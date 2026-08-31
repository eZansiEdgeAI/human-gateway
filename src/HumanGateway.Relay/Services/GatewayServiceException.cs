using HumanGateway.Protocol.Models;

namespace HumanGateway.Relay.Services;

/// <summary>
/// A domain exception raised by the Relay's gateway/rendezvous services that maps to a stable
/// <see cref="ProtocolError"/>-shaped HTTP response (SP-07). Carries the HTTP status, the reserved protocol
/// error <see cref="Code"/>, and the retryability hint. Endpoint handlers stay thin; the global exception
/// handler in <c>Program.cs</c> translates instances via <see cref="HumanGateway.Relay.Api.ApiErrors"/>.
/// </summary>
public sealed class GatewayServiceException : Exception
{
    /// <summary>HTTP status code for the error response.</summary>
    public int StatusCode { get; }

    /// <summary>Machine-readable, stable error code (ErrorCodes catalog).</summary>
    public string Code { get; }

    /// <summary>Retry hint: true for transient conditions, false for permanent rejections.</summary>
    public bool Retryable { get; }

    public GatewayServiceException(int statusCode, string code, string message, bool retryable = false)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Retryable = retryable;
    }

    /// <summary>400 — the request is malformed or fails validation.</summary>
    public static GatewayServiceException BadRequest(string code, string message)
        => new(StatusCodes.Status400BadRequest, code, message);

    /// <summary>403 — the gateway failed identity/registration checks (SP-02).</summary>
    public static GatewayServiceException Forbidden(string code, string message)
        => new(StatusCodes.Status403Forbidden, code, message);

    /// <summary>404 — the referenced identity does not exist.</summary>
    public static GatewayServiceException NotFound(string message)
        => new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, message);

    /// <summary>409 — the request conflicts with an existing durable record.</summary>
    public static GatewayServiceException Conflict(string message)
        => new(StatusCodes.Status409Conflict, ErrorCodes.Conflict, message);
}
