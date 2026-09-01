using HumanGateway.Security;
using Microsoft.Extensions.Logging;

namespace HumanGateway.Edge.Security;

/// <summary>
/// Outbound request-signing handler for Edge↔Relay traffic (AUTH-FR-04, SP-01): signs every request it
/// processes with the gateway's request-signing key (derived from the current registration token) so the Relay
/// can verify that the request genuinely came from this registered gateway. Attach it as the outermost
/// <see cref="HttpMessageHandler"/> on the Edge's Relay-bound <see cref="HttpClient"/> instances (artifact
/// transport, and the future sync transport).
///
/// <para>The signature covers the request method, path, raw query string, an RFC 3339 UTC timestamp, a random
/// nonce, and the gateway id (see <see cref="GatewayRequestSigning"/>). The body is intentionally not hashed —
/// transport integrity comes from TLS (SP-01) and artifact integrity from content hashes (SP-06) — keeping the
/// channel streaming-safe for large artifact chunks.</para>
///
/// <para>The handler sets the <see cref="GatewayRequestSigning.GatewayIdHeader"/>, timestamp, nonce, and
/// signature headers itself, so the Relay can attribute and verify every request without parsing the body or
/// trusting a caller-supplied gateway id.</para>
///
/// <para>When no registration token is available yet (before the registration handshake completes) the request
/// passes through <b>unsigned</b>: the Relay's sync endpoints reject unregistered identities regardless (SP-02),
/// and the handshake itself is not signed (it is how the token is first obtained).</para>
/// </summary>
public sealed class SignedGatewayRequestHandler : DelegatingHandler
{
    private readonly string _gatewayId;
    private readonly Func<string?> _registrationTokenProvider;
    private readonly ILogger<SignedGatewayRequestHandler> _logger;
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates the handler over the gateway's durable id and a provider of its current plaintext registration
    /// token (e.g. a closure over <see cref="GatewayIdentityManager"/>). A <see langword="null"/> token means
    /// "not registered yet" and the request is passed through unsigned.
    /// </summary>
    public SignedGatewayRequestHandler(
        string gatewayId,
        Func<string?> registrationTokenProvider,
        ILogger<SignedGatewayRequestHandler> logger,
        TimeProvider? time = null)
    {
        _gatewayId = gatewayId ?? throw new ArgumentNullException(nameof(gatewayId));
        _registrationTokenProvider = registrationTokenProvider ?? throw new ArgumentNullException(nameof(registrationTokenProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _registrationTokenProvider();
        if (string.IsNullOrWhiteSpace(token))
        {
            // Registration bootstrap: no token yet. The Relay's sync endpoints reject unregistered identities
            // regardless (SP-02); the registration handshake itself is how the token is first obtained.
            _logger.LogDebug("No registration token yet; sending request unsigned (registration bootstrap)");
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var signingKey = GatewayRequestSigning.DeriveKey(token);
        GatewayRequestSigning.SignRequest(request, _gatewayId, signingKey, _time);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
