using System.Text.Json;
using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Options;
using HumanGateway.Relay.Services;
using HumanGateway.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HumanGateway.Relay.Security;

/// <summary>
/// Request-authentication middleware for Edge↔Relay traffic (AUTH-FR-04, SP-01): every request under
/// <c>/sync/*</c> (the sync push/pull endpoints and the artifact byte channel) must be signed with the
/// gateway's request-signing key. The signature covers the request method, path, raw query string, an RFC 3339
/// UTC timestamp, a random nonce, and the gateway id (see <see cref="GatewayRequestSigning"/>), so it cannot be
/// forged, replayed beyond the timestamp skew window, or rebound to a different gateway or endpoint.
///
/// <para><b>Order of checks (defence-in-depth, SP-02).</b> The request's claimed gateway identity is resolved
/// from the <c>gatewayId</c> query parameter, or from the <c>gatewayId</c> field of a JSON body (sync batches,
/// pull requests, the artifact dedup-state check). An unknown identity is rejected 404 and a non-REGISTERED
/// identity is rejected 403 with the reserved state codes — <em>before</em> any signature work, so the Relay
/// never reveals signing material for identities it does not trust. Only a REGISTERED gateway's request is
/// then required to carry a valid signature; any missing, stale, or mismatched signature is rejected 401
/// <c>SIGNATURE_INVALID</c>.</para>
///
/// <para>On success the authenticated gateway id is exposed under <see cref="GatewayIdentityKey"/> so endpoint
/// handlers can trust it without re-deriving it from the body.</para>
/// </summary>
public sealed class GatewayRequestAuthenticationMiddleware
{
    /// <summary><see cref="HttpContext.Items"/> key holding the authenticated gateway id for a signed request.</summary>
    public const string GatewayIdentityKey = "HumanGateway.Relay.AuthenticatedGatewayId";

    private const string SyncPathPrefix = "/sync/";

    private readonly RequestDelegate _next;
    private readonly TimeSpan _maxSkew;

    /// <summary>Creates the middleware over the next delegate and the configured signature skew window.</summary>
    public GatewayRequestAuthenticationMiddleware(
        RequestDelegate next, IOptions<RelayOptions> options)
    {
        _next = next;
        _maxSkew = TimeSpan.FromMinutes(Math.Max(1, options.Value.RequestSignatureSkewMinutes));
    }

    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (request.Path.Value is not { } path || !path.StartsWith(SyncPathPrefix, StringComparison.Ordinal))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Resolve the gateway identity the request claims: the gatewayId query parameter (artifact byte
        // channel) or the gatewayId field of a JSON body (sync push/pull, dedup-state check).
        var claimed = await ResolveClaimedGatewayIdAsync(request, context.RequestAborted).ConfigureAwait(false);
        if (claimed is null)
        {
            // Cannot attribute the request — the endpoint's own validation will reject it.
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Identity-confusion guard (defence-in-depth, AUTH-FR-04): when the request carries a signed gateway
        // identity header, the claimed gatewayId in the query/body must agree with it, so a signed request
        // cannot be rebound to a different gateway. Checked before the store lookup so a mismatched identity
        // is rejected without revealing whether the claimed gateway exists.
        var signedGatewayId = request.Headers[GatewayRequestSigning.GatewayIdHeader].ToString();
        if (!string.IsNullOrWhiteSpace(signedGatewayId)
            && !string.Equals(signedGatewayId, claimed, StringComparison.Ordinal))
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized, ErrorCodes.SignatureInvalid,
                "The request gatewayId does not match the signed gateway identity (AUTH-FR-04).")
                .ConfigureAwait(false);
            return;
        }

        var gatewayService = context.RequestServices.GetRequiredService<GatewayService>();
        var record = await gatewayService.FindAsync(claimed, context.RequestAborted).ConfigureAwait(false);

        // SP-02: unregistered / non-REGISTERED identities are rejected regardless of signature.
        if (record is null)
        {
            await RejectAsync(context, StatusCodes.Status404NotFound, ErrorCodes.NotFound,
                $"Gateway '{claimed}' is not registered (SP-02).").ConfigureAwait(false);
            return;
        }

        if (!IsRegistered(record))
        {
            await RejectAsync(context, StatusCodes.Status403Forbidden, StateCode(record),
                $"Gateway '{claimed}' cannot perform this operation (SP-02).").ConfigureAwait(false);
            return;
        }

        // A REGISTERED gateway's request must be signed and fresh.
        if (!TryVerifySignature(request, record.RequestSigningKey, claimed))
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized, ErrorCodes.SignatureInvalid,
                "The request signature is missing, stale, or invalid (AUTH-FR-04).").ConfigureAwait(false);
            return;
        }

        context.Items[GatewayIdentityKey] = claimed;
        await _next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the gateway id the request claims: the <c>gatewayId</c> query parameter when present, otherwise
    /// the top-level <c>gatewayId</c> of a JSON request body. Returns null when neither carries one.
    /// </summary>
    private static async Task<string?> ResolveClaimedGatewayIdAsync(HttpRequest request, CancellationToken ct)
    {
        var query = request.Query["gatewayId"].ToString();
        if (!string.IsNullOrWhiteSpace(query))
        {
            return query;
        }

        if (!HttpMethods.IsPost(request.Method) && !HttpMethods.IsPut(request.Method))
        {
            return null;
        }

        var contentType = request.ContentType ?? string.Empty;
        if (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return null; // raw byte body (artifact chunk) — the gateway id lives in the query for these.
        }

        try
        {
            // Buffer + rewind so the endpoint still reads the full body afterwards. Async-only: the ASP.NET
            // pipeline forbids synchronous stream IO (the test host throws on Read).
            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            request.Body.Position = 0;

            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("gatewayId", out var property)
                ? property.GetString()
                : null;
        }
        catch (JsonException)
        {
            request.Body.Position = 0;
            return null;
        }
    }

    /// <summary>
    /// Verifies the request signature against the stored request-signing key. The canonical string is rebuilt
    /// from the actual request (method, decoded path, raw query) plus the presented timestamp, nonce, and the
    /// claimed gateway id — byte-identical to what the Edge signed (AUTH-FR-04).
    /// </summary>
    private bool TryVerifySignature(HttpRequest request, string? requestSigningKey, string gatewayId)
    {
        if (string.IsNullOrWhiteSpace(requestSigningKey))
        {
            // An identity registered before the request-signing-key field existed cannot produce a verifiable
            // signature; treat it as unsigned (the Edge must rotate to obtain a signing key).
            return false;
        }

        var timestamp = request.Headers[GatewayRequestSigning.TimestampHeader].ToString();
        var nonce = request.Headers[GatewayRequestSigning.NonceHeader].ToString();
        var signature = request.Headers[GatewayRequestSigning.SignatureHeader].ToString();

        if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(nonce) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (!GatewayRequestSigning.IsFresh(timestamp, DateTimeOffset.UtcNow, _maxSkew))
        {
            return false;
        }

        var canonical = GatewayRequestSigning.Canonicalize(
            request.Method, request.Path.Value ?? string.Empty, request.QueryString.Value ?? string.Empty,
            timestamp, nonce, gatewayId);

        return GatewayRequestSigning.Verify(requestSigningKey, canonical, signature);
    }

    private static bool IsRegistered(HumanGateway.Relay.Storage.Entities.GatewayRecord record)
        => record.Status == "REGISTERED";

    private static string StateCode(HumanGateway.Relay.Storage.Entities.GatewayRecord record)
        => record.Status switch
        {
            "SUSPENDED" => ErrorCodes.GatewaySuspended,
            "REVOKED" => ErrorCodes.GatewayRevoked,
            _ => ErrorCodes.GatewayUnregistered,
        };

    private static Task RejectAsync(HttpContext context, int statusCode, string code, string message)
    {
        var error = new ProtocolError
        {
            Code = code,
            Message = message,
            Retryable = false,
        };
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(error, context.RequestAborted);
    }
}

/// <summary>Convenience extension for wiring <see cref="GatewayRequestAuthenticationMiddleware"/>.</summary>
public static class GatewayRequestAuthenticationExtensions
{
    /// <summary>
    /// Adds signed-request authentication for Edge↔Relay traffic (AUTH-FR-04). Must be added before the endpoint
    /// map so <c>/sync/*</c> requests are verified before any handler runs.
    /// </summary>
    public static IApplicationBuilder UseGatewayRequestAuthentication(this IApplicationBuilder app)
        => app.UseMiddleware<GatewayRequestAuthenticationMiddleware>();
}
