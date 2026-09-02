using System.Diagnostics;
using System.Text.Json;
using HumanGateway.Relay.Api;
using HumanGateway.Relay.Endpoints;
using HumanGateway.Relay.Health;
using HumanGateway.Relay.Options;
using HumanGateway.Relay.Security;
using HumanGateway.Relay.Services;
using HumanGateway.Relay.Storage;
using HumanGateway.Security;
using HumanGateway.Core.Artifacts;
using HumanGateway.Core.Idempotency;
using HumanGateway.Core.Inbox;
using HumanGateway.Core.Outbox;
using HumanGateway.Core.Sync;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Console;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// PostgreSQL store (RELAY-FR-01): the durable cloud schema for gateways,
// conversations, messages, deliveries, artifacts (metadata + BYTEA blobs), and
// the sync model. The connection string is resolved lazily inside the context
// factory (below) from the built configuration — set it via configuration
// (ConnectionStrings:Relay — from the environment in deployment; the
// development default matches the README's dev PostgreSQL).
// ---------------------------------------------------------------------------

// Register a pooled-context factory rather than a scoped DbContext: the durable
// inbox/idempotency/cursor stores are long-lived singletons (driven by the sync
// endpoint and the background worker) and open a short-lived context per
// operation.
//
// The connection string is resolved lazily from the *built* configuration (via
// the provider) rather than captured into a local at startup: the test harness
// (WebApplicationFactory) layers its configuration override in during host
// construction, and reading `builder.Configuration` there would snapshot the
// pre-override value. Reading it at context-creation time guarantees the store
// always uses the final connection string.
builder.Services.AddDbContextFactory<RelayDbContext>((sp, options) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var relayConnection = configuration.GetConnectionString("Relay");
    if (string.IsNullOrWhiteSpace(relayConnection))
    {
        relayConnection = "Host=localhost;Port=5432;Database=humangateway_relay;Username=humangateway;Password=humangateway";
    }

    options.UseNpgsql(relayConnection);
});

// ---------------------------------------------------------------------------
// Relay HTTP API (RELAY-FR-02). The JSON layer shares the protocol wire contract
// (exact enum tokens, omit-null, disallow unmapped) so the entities the API
// returns are byte-identical to their canonical wire form.
// ---------------------------------------------------------------------------
builder.Services.ConfigureHttpJsonOptions(options => RelayJson.Configure(options.SerializerOptions));

// ---------------------------------------------------------------------------
// Structured logging (NF-09): every Relay log is a structured message template with named fields (the
// services log gateway ids, batch ids, cursors, and durations as fields — never secrets, SP-07). The JSON
// console formatter is registered so deployments select machine-parseable logs via
// `Logging__Console__FormatterName=json` (the appsettings default) — or the human-readable formatter with
// `=simple` — with stable UTC timestamps either way.
// ---------------------------------------------------------------------------
builder.Logging.AddJsonConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.UseUtcTimestamp = true;
    options.IncludeScopes = true;
    options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
});

// Relay behaviour options (token TTL, rendezvous online window) — bound from the "Relay" section.
builder.Services.Configure<RelayOptions>(builder.Configuration.GetSection(RelayOptions.SectionName));

// Gateway registration + rendezvous services (RELAY-FR-03, WEBX-FR-02). Scoped: each request opens its own
// short-lived context via the pooled factory.
builder.Services.AddScoped<GatewayService>();
builder.Services.AddScoped<RendezvousService>();

// ---------------------------------------------------------------------------
// Remote user identity + authentication (IDENTITY-SECURITY-5.2, AUTH-FR-02, SP-03, external-web-access):
// remote users authenticate at the Relay with a username + password; successful logins issue signed opaque
// session tokens (hgsu_ + 256 bits, Open Q #1 default) — the same semantics as the Edge via the shared
// IUserSessionService contract. The bootstrap user is seeded from configuration (env/secret store in
// deployment — a committed password is a release-blocker, SP-07). Singleton, like the other durable ports
// (it opens a short-lived context per operation via the pooled factory) — this also lets the bearer-session
// middleware resolve it from the root provider.
// ---------------------------------------------------------------------------
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddSingleton<RemoteAuthService>();
builder.Services.AddSingleton<IUserSessionService>(sp => sp.GetRequiredService<RemoteAuthService>());

// ---------------------------------------------------------------------------
// Sync engine + durable ports (RELAY-FR-02, SYNC-FR-01..07). The Relay consumes the shared SyncEngine
// contract (product vision §6.3) over its own PostgreSQL ports: RelayInbox (applied PUSH items), RelayOutbox
// (the per-gateway PULL queue), and RelayIdempotencyStore (batch dedup). The ports are stateless singletons
// that open a short-lived context per operation; RelaySyncService drives the engine from the sync endpoints.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<RelayInbox>();
builder.Services.AddSingleton<RelayOutbox>();
builder.Services.AddSingleton<RelayIdempotencyStore>();
builder.Services.AddSingleton<IOutbox>(sp => sp.GetRequiredService<RelayOutbox>());
builder.Services.AddSingleton<IInbox>(sp => sp.GetRequiredService<RelayInbox>());
builder.Services.AddSingleton<IIdempotencyStore>(sp => sp.GetRequiredService<RelayIdempotencyStore>());
builder.Services.AddSingleton<ISyncEngine, SyncEngine>();
builder.Services.AddScoped<RelaySyncService>();

// ---------------------------------------------------------------------------
// Content-addressed artifact bytes (RELAY-FR-01, ARTF-FR-01): the Relay's IArtifactStore port over the
// artifact_blobs BYTEA table with streaming reads (PostgresArtifactStore). Singleton, like the other
// durable ports — each operation opens a short-lived context from the pooled factory. An S3-compatible
// adapter is an optional later step, not v1 (cloud-relay Open Q #2, NF-10).
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<PostgresArtifactStore>();
builder.Services.AddSingleton<IArtifactStore>(sp => sp.GetRequiredService<PostgresArtifactStore>());

// ---------------------------------------------------------------------------
// Artifact byte channel (RELAY-FR-01, ARTF-FR-01/02/03): dedup state, resumable chunked upload with
// content-hash verification, and streaming download — every operation gated on a registered gateway (SP-02).
// Scoped: each request opens its own short-lived context; the in-memory partial-upload state is static.
// ---------------------------------------------------------------------------
builder.Services.AddScoped<RelayArtifactService>();

// ---------------------------------------------------------------------------
// Health checks (NF-09, RELAY-FR-05): the durable-store round-trip (RelayStoreHealthCheck) plus the relay
// sync-health snapshot (RelaySyncHealthCheck — registered/online gateways, queued cross-site items).
// Surfaced to admins as a detailed report on /health; /healthz reduces them to a liveness 200/503 for the
// compose/orchestrator probe.
// ---------------------------------------------------------------------------
builder.Services.AddHealthChecks()
    .AddCheck<RelayStoreHealthCheck>("store")
    .AddCheck<RelaySyncHealthCheck>("sync");

var app = builder.Build();

// Request observability (NF-09): one structured log per request — method, path (never query strings, so
// nothing sensitive), final status, and duration. Health probes are polled continuously (compose,
// orchestrators), so they are skipped to keep the operational log readable. Placed outermost so the final
// status — including 500s produced by the exception handler — is the one observed.
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (path is "/healthz" or "/health")
    {
        await next();
        return;
    }

    var stopwatch = Stopwatch.StartNew();
    try
    {
        await next();
    }
    finally
    {
        stopwatch.Stop();
        app.Logger.LogInformation("HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs}ms",
            context.Request.Method, path, context.Response.StatusCode, stopwatch.ElapsedMilliseconds);
    }
});

// TLS everywhere (AUTH-FR-04, SP-01): outside Development, the Relay only serves HTTPS — HTTP requests are
// redirected to HTTPS and responses advertise HSTS. Local development (tests, `dotnet run`) keeps plain HTTP
// so the loopback/dev flow needs no certificate. Deployments terminating TLS at a proxy must forward the
// original scheme (ForwardedHeaders) so the redirect/HSTS behave correctly.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Public PWA entry point (WEBX-FR-01): the Relay serves the exact build used on
// the Edge. Static files are deliberately installed before endpoint mapping;
// the fallback is added below, after the API endpoints, so an API typo cannot
// silently receive index.html.
app.UseDefaultFiles();
app.UseStaticFiles();

// Translate unexpected exceptions into ProtocolError-shaped responses so Edge
// Gateways always receive the stable, machine-readable error contract (SP-07).
// Unhandled exceptions are logged at Error with the request path for diagnosis.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var exception = feature?.Error;
    if (exception is not null)
    {
        app.Logger.LogError(exception, "Unhandled exception while processing {Path}", context.Request.Path.Value);
    }

    var result = exception is null ? ApiErrors.InternalError() : ApiErrors.FromException(exception);
    await result.ExecuteAsync(context);
}));

// Startup lifecycle (NF-09): the version, environment, and durable-store target are logged once on boot.
// Only the database name and host are logged — never the connection string, which carries the password
// (SP-07).
app.Logger.LogInformation("Relay starting: version {Version}, environment {Environment}",
    typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
    app.Environment.EnvironmentName);
if (app.Configuration.GetConnectionString("Relay") is { } relayConnectionString)
{
    try
    {
        var connection = new NpgsqlConnectionStringBuilder(relayConnectionString);
        app.Logger.LogInformation("Relay store: database {Database} at {Host}", connection.Database, connection.Host);
    }
    catch (Exception ex) when (ex is FormatException or ArgumentException)
    {
        // A malformed connection string must never crash the Relay — and must never be logged wholesale
        // (or echoed via the exception message, which some providers populate from keyword values): it
        // carries the password (SP-07).
        app.Logger.LogWarning("Relay store: connection string present but malformed (password never logged)");
    }
}

// Bearer-session authentication (AUTH-FR-02, SP-03): resolves the current remote user from
// `Authorization: Bearer <token>` for /auth/me and future authorised endpoints.
app.UseSessionAuthentication();

// Signed-request authentication for Edge↔Relay traffic (IDENTITY-SECURITY-5.4, AUTH-FR-04): every /sync/*
// request (sync push/pull + artifact byte channel) must be signed with the gateway's request-signing key.
// Runs after the exception handler so ProtocolError-shaped rejections are emitted directly (SP-07), and
// before the endpoints. Registration (/gateways) and remote-auth (/auth) endpoints authenticate by their own
// token/session mechanisms.
app.UseGatewayRequestAuthentication();

// Apply pending EF Core migrations so the schema exists before the Relay begins
// accepting sync traffic (RELAY-FR-01).
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<RelayDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.Migrate();

    // Seed the configured bootstrap remote user (AUTH-FR-02, SP-07: credentials from env/secret store; no-op
    // when none is configured). Runs after the schema exists so the first remote login has an account.
    await scope.ServiceProvider.GetRequiredService<RemoteAuthService>()
        .SeedBootstrapUserAsync(CancellationToken.None);
}

// Health probe endpoints (NF-09, RELAY-FR-05).
// /healthz — liveness for compose/orchestrator probes: 200 when the store round-trip succeeds, 503 when
// degraded (includes a cheap store round-trip via the health checks, so the probe reflects durable-store
// availability, not just process liveness).
app.MapGet("/healthz", async (HealthCheckService health, CancellationToken ct) =>
{
    var report = await health.CheckHealthAsync(ct);
    if (report.Status == HealthStatus.Healthy)
    {
        return Results.Ok(new { status = "ok", store = "postgres" });
    }

    var failed = string.Join(", ",
        report.Entries.Where(e => e.Value.Status != HealthStatus.Healthy).Select(e => e.Key));
    app.Logger.LogWarning("Health probe degraded: {Status} (failed checks: {FailedChecks})", report.Status, failed);
    return Results.Json(new { status = "degraded", store = "postgres" },
        statusCode: StatusCodes.Status503ServiceUnavailable);
});

// /health — detailed, admin-facing report (NF-09: "sync health surfaced to admins"): per-check status,
// latency, and health data (store round-trip, registered/online gateways, queued cross-site items).
// Degraded/Unhealthy returns 503 so monitors and load balancers alarm.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthReportJson.WriteAsync,
    AllowCachingResponses = false,
    ResultStatusCodes = new Dictionary<HealthStatus, int>
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    },
});

// The Relay API: gateway registration + rendezvous, sync, remote auth, service info.
app.MapRemoteAuthEndpoints();
app.MapRelayEndpoints();
app.MapWebAppEndpoints();

app.Run();

app.Logger.LogInformation("Relay stopped");

// Exposes the entry point to the test project (WebApplicationFactory<Program>).
public partial class Program
{
}
