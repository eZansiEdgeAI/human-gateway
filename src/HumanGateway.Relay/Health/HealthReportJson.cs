using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HumanGateway.Relay.Health;

/// <summary>
/// Writes the ASP.NET Core <see cref="HealthReport"/> as a compact, machine-readable JSON document for the
/// admin-facing <c>/health</c> endpoint (NF-09). Shape:
/// <code>
/// {
///   "status": "Healthy",
///   "generatedAt": "2026-08-31T21:40:00Z",
///   "checks": [
///     { "name": "store", "status": "Healthy", "durationMs": 3, "data": { "roundTripMs": 3 } },
///     { "name": "sync",  "status": "Healthy", "durationMs": 5, "data": { ... } }
///   ]
/// }
/// </code>
/// <c>data</c> is omitted when a check reports none. No secrets are ever included.
/// </summary>
public static class HealthReportJson
{
    /// <summary>The response writer invoked by <c>MapHealthChecks</c> for the <c>/health</c> report.</summary>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            generatedAt = DateTimeOffset.UtcNow,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
                data = entry.Value.Data.Count > 0 ? entry.Value.Data : null,
            }),
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        return JsonSerializer.SerializeAsync(context.Response.Body, payload, options, context.RequestAborted);
    }
}
