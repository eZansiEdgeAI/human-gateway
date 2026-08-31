using HumanGateway.Relay.Api;
using HumanGateway.Relay.Endpoints;
using HumanGateway.Relay.Storage;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// PostgreSQL store (RELAY-FR-01): the durable cloud schema for gateways,
// conversations, messages, deliveries, artifacts (metadata + BYTEA blobs), and
// the sync model. The connection string is taken from configuration
// (ConnectionStrings:Relay — set it from the environment in deployment; the
// development default matches the README's dev PostgreSQL).
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Relay");
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = "Host=localhost;Port=5432;Database=humangateway_relay;Username=humangateway;Password=humangateway";
}

// Register a pooled-context factory rather than a scoped DbContext: the durable
// inbox/idempotency/cursor stores are long-lived singletons (driven by the sync
// endpoint and the background worker) and open a short-lived context per
// operation.
builder.Services.AddDbContextFactory<RelayDbContext>((sp, options) =>
{
    options.UseNpgsql(connectionString);
});

// ---------------------------------------------------------------------------
// Relay HTTP API (RELAY-FR-02). The JSON layer shares the protocol wire contract
// (exact enum tokens, omit-null, disallow unmapped) so the entities the API
// returns are byte-identical to their canonical wire form.
// ---------------------------------------------------------------------------
builder.Services.ConfigureHttpJsonOptions(options => RelayJson.Configure(options.SerializerOptions));

var app = builder.Build();

// Translate unexpected exceptions into ProtocolError-shaped responses so Edge
// Gateways always receive the stable, machine-readable error contract (SP-07).
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var exception = feature?.Error;
    var result = exception is null ? ApiErrors.InternalError() : ApiErrors.FromException(exception);
    await result.ExecuteAsync(context);
}));

// Apply pending EF Core migrations so the schema exists before the Relay begins
// accepting sync traffic (RELAY-FR-01).
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<RelayDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.Migrate();
}

// Health probe (RELAY-FR-05 structured logging + health endpoint): includes a
// cheap store round-trip so the probe reflects durable-store availability, not
// just process liveness.
app.MapGet("/healthz", async (IDbContextFactory<RelayDbContext> factory, CancellationToken ct) =>
{
    try
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.CanConnectAsync(ct);
        return Results.Ok(new { status = "ok", store = "postgres" });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Health probe store check failed");
        return Results.Json(new { status = "degraded", store = "postgres" }, statusCode: 503);
    }
});

// The Relay API: gateway registration + rendezvous, sync, service info.
app.MapRelayEndpoints();

app.Run();

// Exposes the entry point to the test project (WebApplicationFactory<Program>).
public partial class Program
{
}
