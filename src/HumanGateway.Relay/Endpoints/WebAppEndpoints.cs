namespace HumanGateway.Relay.Endpoints;

/// <summary>
/// Hosts the same offline-first PWA as the public Relay (WEBX-FR-01). The
/// static-file middleware is configured in <c>Program.cs</c>; this endpoint
/// supplies the client-side route fallback without turning API failures into
/// HTML responses.
/// </summary>
public static class WebAppEndpoints
{
    /// <summary>Maps the client-side route fallback after all API endpoints.</summary>
    public static void MapWebAppEndpoints(this WebApplication app)
    {
        app.MapFallback("{*path:nonfile}", static (HttpContext context, IWebHostEnvironment environment) =>
        {
            // Do not hide an unknown API route behind the client shell. This is
            // also important for fetch callers, which must receive a 404 rather
            // than a successful HTML document.
            if (IsApiPath(context.Request.Path))
            {
                return Results.NotFound();
            }

            var indexPath = Path.Combine(environment.WebRootPath ?? string.Empty, "index.html");
            return Results.File(indexPath, "text/html");
        });
    }

    private static bool IsApiPath(PathString path)
    {
        var value = path.Value ?? string.Empty;
        return value.Equals("/relay", StringComparison.OrdinalIgnoreCase)
            || IsPathOrChildOf(value, "/auth")
            || IsPathOrChildOf(value, "/gateways")
            || IsPathOrChildOf(value, "/remote")
            || IsPathOrChildOf(value, "/sync")
            || IsPathOrChildOf(value, "/artifacts")
            || value.StartsWith("/health", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathOrChildOf(string value, string root)
        => value.Equals(root, StringComparison.OrdinalIgnoreCase)
            || value.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
}
