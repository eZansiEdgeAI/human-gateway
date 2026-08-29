using HumanGateway.Core.Idempotency;
using HumanGateway.Core.Inbox;
using HumanGateway.Core.Outbox;
using HumanGateway.Core.Sync;
using HumanGateway.Edge.Api;
using HumanGateway.Edge.Endpoints;
using HumanGateway.Edge.Storage;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// SQLite store (LOCAL-EDGE-1.2, EDGE-FR-02): the durable local schema for
// conversations, messages, deliveries, tasks, artifacts, and participants. The
// SqlitePragmaInterceptor applies WAL + synchronous=NORMAL (and per-connection
// foreign_keys/busy_timeout) on every open (EDGE-FR-06/07, NF-04).
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Edge");
if (string.IsNullOrWhiteSpace(connectionString))
{
    var dataDir = Path.Combine(builder.Environment.ContentRootPath, "data");
    Directory.CreateDirectory(dataDir);
    connectionString = SqliteConnectionFactory.BuildConnectionString(Path.Combine(dataDir, "edge.db"));
}

builder.Services.AddSingleton<SqlitePragmaInterceptor>();

// Register a pooled-context factory rather than a scoped DbContext: the durable
// inbox/outbox/idempotency stores are long-lived singletons (driven by the sync
// worker, a hosted service) and open a short-lived context per operation. Each
// context gets the WAL + synchronous=NORMAL pragmas via the interceptor (NF-04).
builder.Services.AddDbContextFactory<EdgeDbContext>((sp, options) =>
{
    options.UseSqlite(connectionString);
    options.AddInterceptors(sp.GetRequiredService<SqlitePragmaInterceptor>());
});

// ---------------------------------------------------------------------------
// Durable inbox/outbox/idempotency (LOCAL-EDGE-1.3, EDGE-FR-04). Every create is
// committed to SQLite before any network attempt. The SyncEngine drives these
// ports; swapping the in-memory reference implementations for the SQLite-backed
// stores required no change to the engine or to the endpoints below.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<IOutbox, SqliteOutbox>();
builder.Services.AddSingleton<IInbox, SqliteInbox>();
builder.Services.AddSingleton<IIdempotencyStore, SqliteIdempotencyStore>();
builder.Services.AddSingleton<ISyncEngine, SyncEngine>();

// ---------------------------------------------------------------------------
// Local REST API (LOCAL-EDGE-1.4, EDGE-FR-03). The HTTP JSON layer shares the
// protocol wire contract (exact enum tokens, omit-null, disallow unmapped) so the
// entities the API returns are byte-identical to their canonical wire form.
// ---------------------------------------------------------------------------
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.AddSingleton<LocalApiService>();
builder.Services.ConfigureHttpJsonOptions(options => LocalApiJson.Configure(options.SerializerOptions));

var app = builder.Build();

// Translate domain/validation exceptions into ProtocolError-shaped responses so
// the PWA always receives the stable, machine-readable error contract (SP-07).
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var exception = feature?.Error;
    var result = exception is null ? ApiErrors.InternalError() : ApiErrors.FromException(exception);
    await result.ExecuteAsync(context);
}));

// Apply pending EF Core migrations so the schema exists before the Edge begins
// serving the LAN (lifecycle STARTING -> STARTED, product vision §10).
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<EdgeDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.Migrate();
}

// Local health probe (EDGE-FR-01): reachable with no Internet. Includes a cheap
// store round-trip so the probe reflects durable-store availability, not just
// process liveness.
app.MapGet("/healthz", async (IDbContextFactory<EdgeDbContext> factory, CancellationToken ct) =>
{
    try
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.CanConnectAsync(ct);
        return Results.Ok(new { status = "ok", store = "sqlite" });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Health probe store check failed");
        return Results.Json(new { status = "degraded", store = "sqlite" }, statusCode: 503);
    }
});

// The local REST API: conversations, messages, tasks, artifacts, sync status.
app.MapLocalApiEndpoints();

app.Run();

// Exposes the entry point to the test project (WebApplicationFactory<Program>).
public partial class Program
{
}
