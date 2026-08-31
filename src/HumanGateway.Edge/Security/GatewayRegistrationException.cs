using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HumanGateway.Protocol.Models;

namespace HumanGateway.Edge.Security;

/// <summary>
/// Raised by the Edge registration client when the Relay rejects a registration/rotation attempt (SP-02).
/// Carries the protocol error <see cref="Code"/> and the retryability hint so the identity manager can decide
/// between retrying and surfacing a permanent rejection. The message never contains the registration token
/// (SP-07).
/// </summary>
public sealed class GatewayRegistrationException : Exception
{
    /// <summary>HTTP status code returned by the Relay.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Machine-readable protocol error code (ErrorCodes catalog), when the Relay returned one.</summary>
    public string? Code { get; }

    /// <summary>Retry hint from the Relay error (true for transient conditions).</summary>
    public bool Retryable { get; }

    public GatewayRegistrationException(string message, HttpStatusCode statusCode = HttpStatusCode.BadGateway,
        string? code = null, bool retryable = false, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        Code = code;
        Retryable = retryable;
    }

    /// <summary>Translates a non-success Relay response into a <see cref="GatewayRegistrationException"/>.</summary>
    public static async Task<GatewayRegistrationException> FromResponseAsync(
        HttpResponseMessage response, string action, CancellationToken ct)
    {
        // Best-effort parse of the ProtocolError-shaped body; a malformed/empty body still yields a usable error.
        ProtocolError? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ProtocolError>(ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // The Relay always returns ProtocolError-shaped bodies; an unparseable one is still surfaced
            // generically below (and never contains a registration token).
        }

        // The Relay's reserved registration/gateway codes map directly (SP-02, SP-07).
        var code = error?.Code;
        var retryable = error?.Retryable ?? false;
        var status = response.StatusCode;
        var message = code is not null
            ? $"The Relay rejected the attempt to {action} (HTTP {(int)status}, {code})."
            : $"The Relay rejected the attempt to {action} (HTTP {(int)status}).";
        return new GatewayRegistrationException(message, status, code, retryable);
    }
}
