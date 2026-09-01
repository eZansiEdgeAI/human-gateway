using HumanGateway.Security;

namespace HumanGateway.Relay.Tests;

/// <summary>
/// Test-only outbound request-signing handler (AUTH-FR-04): signs every request for which a gateway signing
/// key is registered, mirroring the production <c>SignedGatewayRequestHandler</c>. The gateway id is located in
/// the query string (artifact-byte endpoints) or the JSON body (sync push/pull); requests whose gateway has no
/// registered key pass through <b>unsigned</b> so the Relay's auth boundary is exercised in tests exactly as in
/// production. Keys are derived from the one-time registration token returned by <c>POST /gateways</c>.
/// </summary>
public sealed class TestSigningHandler : DelegatingHandler
{
    /// <summary>Per-gateway request-signing keys (gatewayId → derived HMAC key), populated by the registration helpers.</summary>
    public IDictionary<string, string> Keys { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var gatewayId = await FindGatewayIdAsync(request, ct).ConfigureAwait(false);
        if (gatewayId is not null && Keys.TryGetValue(gatewayId, out var key))
        {
            GatewayRequestSigning.SignRequest(request, gatewayId, key, TimeProvider.System);
        }

        return await base.SendAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>Locates the request's gateway id: query string first (artifacts), then the JSON body (sync).</summary>
    private static async Task<string?> FindGatewayIdAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is { } uri && !string.IsNullOrEmpty(uri.Query))
        {
            var fromQuery = FindInQuery(uri.Query, "gatewayId");
            if (!string.IsNullOrEmpty(fromQuery))
            {
                return fromQuery;
            }
        }

        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (body.Length > 0 && body[0] == '{')
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("gatewayId", out var id))
                {
                    return id.GetString();
                }
            }
        }

        return null;
    }

    private static string? FindInQuery(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = eq < 0 ? pair : pair[..eq];
            if (string.Equals(key, name, StringComparison.Ordinal))
            {
                var value = eq < 0 ? string.Empty : pair[(eq + 1)..];
                return Uri.UnescapeDataString(value);
            }
        }

        return null;
    }
}
