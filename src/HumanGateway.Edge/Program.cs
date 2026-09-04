using HumanGateway.Core.Artifacts;
using HumanGateway.Core.Cursor;
using HumanGateway.Core.Idempotency;
using HumanGateway.Core.Inbox;
using HumanGateway.Core.Outbox;
using HumanGateway.Core.Sync;
using HumanGateway.Edge.Api;
using HumanGateway.Edge.Artifacts;
using HumanGateway.Edge.Endpoints;
using HumanGateway.Edge.Security;
using HumanGateway.Edge.Storage;
using HumanGateway.Edge.Sync;
using HumanGateway.Security;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// The Vite development server runs on a separate localhost origin. Keep this
// allowlist development-only; production deployments should serve the PWA
// through the configured trusted origin instead of enabling broad CORS.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options => options.AddPolicy("PwaDevelopment", policy =>
        policy.WithOrigins(
                "http://localhost:5173",
                "http://127.0.0.1:5173",
                "http://localhost:4173",
                "http://127.0.0.1:4173")
            .AllowAnyHeader()
            .AllowAnyMethod()));
}

// ---------------------------------------------------------------------------
// SQLite store (LOCAL-EDGE-1.2, EDGE-FR-02): the durable local schema for
// conversations, messages, deliveries, tasks, artifacts, and participants. The
// SqlitePragmaInterceptor applies WAL + synchronous=NORMAL (and per-connection
// foreign_keys/busy_timeout) on every open (EDGE-FR-06/07, NF-04). The data
// directory also roots the gateway secret store (SP-07), so it is resolved once
// and shared below.
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Edge");
var configuredDataDir = builder.Configuration.GetValue<string>(
    $"{GatewayOptions.SectionName}:{nameof(GatewayOptions.DataDirectory)}");

string dataDir;
if (string.IsNullOrWhiteSpace(connectionString))
{
    // The Edge owns the default connection string → root durable state (SQLite + gateway secret store) in
    // the configured data directory, or <ContentRoot>/data when unset (gitignored, SP-07).
    dataDir = !string.IsNullOrWhiteSpace(configuredDataDir)
        ? configuredDataDir
        : Path.Combine(builder.Environment.ContentRootPath, "data");
    Directory.CreateDirectory(dataDir);
    connectionString = SqliteConnectionFactory.BuildConnectionString(Path.Combine(dataDir, "edge.db"));
}
else
{
    // A connection string was supplied (tests, containers) → co-locate the secret store with the SQLite
    // database so durable state (DB + gateway identity) shares one gitignored/volume-mounted directory.
    dataDir = SqliteConnectionFactory.DataSourceDirectory(connectionString)
        ?? (!string.IsNullOrWhiteSpace(configuredDataDir)
            ? configuredDataDir
            : Path.Combine(builder.Environment.ContentRootPath, "data"));
    Directory.CreateDirectory(dataDir);
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
builder.Services.AddSingleton<ISyncCursorStore, SqliteSyncCursorStore>();
builder.Services.AddSingleton<ISyncEngine, SyncEngine>();

// ---------------------------------------------------------------------------
// Gateway identity + registration (IDENTITY-SECURITY-5.1, AUTH-FR-01, SP-02):
// the Edge's durable identity (gateway ID + registration token) and the
// two-step registration handshake against the Relay. The plaintext token lives
// only in the owner-only secret store under the data directory (SP-07); the
// registration worker runs before the sync worker and re-confirms/rotates as
// needed. When no Relay is configured the client is disabled and the Edge stays
// LAN-only (SP-01).
//
// Request signing (IDENTITY-SECURITY-5.4, AUTH-FR-04): every outbound
// Edge↔Relay request carries an HMAC signature keyed with the key derived from
// the current registration token (SignedGatewayRequestHandler). The token
// provider reads the identity manager's cached identity, so the very first
// registration request (which obtains the token) passes unsigned — the Relay's
// sync endpoints reject unregistered identities regardless (SP-02). The base URL
// scheme is enforced at startup via RelayTlsPolicy (SP-01): https only, with an
// explicit AllowInsecureHttp opt-out for the local dev compose.
// ---------------------------------------------------------------------------
builder.Services.Configure<GatewayRegistrationOptions>(
    builder.Configuration.GetSection(GatewayRegistrationOptions.SectionName));
builder.Services.Configure<GatewayRegistrationWorkerOptions>(
    builder.Configuration.GetSection(GatewayRegistrationWorkerOptions.SectionName));
builder.Services.AddSingleton<IGatewaySecretStore>(new FileGatewaySecretStore(dataDir));
builder.Services.AddSingleton<GatewayRegistrationClientGate>();
builder.Services.AddSingleton<GatewayIdentityManager>();
builder.Services.AddSingleton<IGatewayRegistrationClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<GatewayRegistrationOptions>>().Value;
    if (!options.Enabled)
    {
        return new DisabledGatewayRegistrationClient();
    }

    // Resolved lazily (Lazy<T>) so the identity manager — which itself depends on this client — can be
    // shared without a DI construction cycle (AUTH-FR-04).
    var manager = new Lazy<GatewayIdentityManager>(sp.GetRequiredService<GatewayIdentityManager>);
    var gateway = sp.GetRequiredService<IOptions<GatewayOptions>>().Value;

    // SP-01: the Relay must be dialed over TLS (https). Fail fast at startup when misconfigured.
    var relayUri = RelayTlsPolicy.RequireAllowed(options.BaseUrl, options.AllowInsecureHttp);

    var http = new HttpClient(new SignedGatewayRequestHandler(
        gateway.GatewayId,
        () => manager.Value.Current?.RegistrationToken,
        sp.GetRequiredService<ILogger<SignedGatewayRequestHandler>>()))
    {
        BaseAddress = new Uri(relayUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute),
        Timeout = options.Timeout,
    };
    return new HttpGatewayRegistrationClient(
        http,
        options,
        sp.GetRequiredService<ILogger<HttpGatewayRegistrationClient>>());
});
builder.Services.AddHostedService<GatewayRegistrationWorker>();

// ---------------------------------------------------------------------------
// Background sync worker (LOCAL-EDGE-1.6, EDGE-FR-05, product vision §10): a
// hosted BackgroundService that periodically dials out to the Relay (SP-01),
// driving the SyncEngine through the outbound IRelaySyncClient hook. The full
// HTTPS transport arrives with the synchronisation feature; until then the
// DisabledRelaySyncClient keeps outbound sync off and the durable outbox retains
// every entry for later sync (local-edge §7 #4). The worker is registered as a
// singleton so its lifecycle state is observable for a future health endpoint
// (NF-09).
// ---------------------------------------------------------------------------
builder.Services.Configure<SyncWorkerOptions>(builder.Configuration.GetSection(SyncWorkerOptions.SectionName));
builder.Services.AddSingleton<IRelaySyncClient, DisabledRelaySyncClient>();
builder.Services.AddSingleton<IInboundMessageHandler, InboundMessageProjector>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SyncWorker>();
builder.Services.AddHostedService(static sp => sp.GetRequiredService<SyncWorker>());

// ---------------------------------------------------------------------------
// Artifact-byte channel to the Relay (ARTF-FR-01/02, PROTO-FR-04 exception): the
// outbound, content-addressed, resumable chunked transfer of artifact bytes. When
// a Relay base URL is configured (Relay:BaseUrl) a real HTTP transport is used;
// otherwise the DisabledArtifactTransfer keeps the Edge offline-first for bytes
// (NF-01). The sync-batch transport is owned by the synchronisation feature.
// ---------------------------------------------------------------------------
builder.Services.Configure<RelayArtifactOptions>(builder.Configuration.GetSection(RelayArtifactOptions.SectionName));
builder.Services.AddSingleton<IArtifactTransfer>(sp =>
{
    var options = sp.GetRequiredService<IOptions<RelayArtifactOptions>>().Value;
    if (!options.Enabled)
    {
        return new DisabledArtifactTransfer();
    }

    // SP-01: https for the byte channel too; same AllowInsecureHttp opt-out for local dev compose.
    var relayUri = RelayTlsPolicy.RequireAllowed(options.BaseUrl, options.AllowInsecureHttp);

    var manager = new Lazy<GatewayIdentityManager>(sp.GetRequiredService<GatewayIdentityManager>);
    var gateway = sp.GetRequiredService<IOptions<GatewayOptions>>().Value;

    var http = new HttpClient(new SignedGatewayRequestHandler(
        gateway.GatewayId,
        () => manager.Value.Current?.RegistrationToken,
        sp.GetRequiredService<ILogger<SignedGatewayRequestHandler>>()))
    {
        BaseAddress = new Uri(relayUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute),
        Timeout = TimeSpan.FromMinutes(15),
    };
    return new HttpArtifactTransport(
        http,
        Options.Create(options),
        sp.GetRequiredService<IOptions<GatewayOptions>>(),
        sp.GetRequiredService<ILogger<HttpArtifactTransport>>());
});

// ---------------------------------------------------------------------------
// Local filesystem artifact store (LOCAL-EDGE-1.5, EDGE-FR-02, ARTF-FR-01):
// content-hash-named files with deduplication, rooted under the configured
// directory (default <ContentRoot>/data/artifacts). Bytes are written
// atomically (temp + rename) so a killed process never leaves a partial
// artifact at a content-addressed path.
// ---------------------------------------------------------------------------
builder.Services.Configure<ArtifactStoreOptions>(builder.Configuration.GetSection(ArtifactStoreOptions.SectionName));
builder.Services.AddSingleton<IArtifactStore>(sp =>
{
    var root = sp.GetRequiredService<IOptions<ArtifactStoreOptions>>().Value.RootPath;
    if (string.IsNullOrWhiteSpace(root))
    {
        root = Path.Combine(builder.Environment.ContentRootPath, "data", "artifacts");
    }

    Directory.CreateDirectory(root);
    return new FilesystemArtifactStore(root);
});

// ---------------------------------------------------------------------------
// Local REST API (LOCAL-EDGE-1.4, EDGE-FR-03). The HTTP JSON layer shares the
// protocol wire contract (exact enum tokens, omit-null, disallow unmapped) so the
// entities the API returns are byte-identical to their canonical wire form.
// ---------------------------------------------------------------------------
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.AddSingleton<LocalApiService>();
builder.Services.ConfigureHttpJsonOptions(options => LocalApiJson.Configure(options.SerializerOptions));

// ---------------------------------------------------------------------------
// User identity + authentication (IDENTITY-SECURITY-5.2, AUTH-FR-02, SP-03): local users
// authenticate at the Edge with a username + password; successful logins issue signed opaque session
// tokens (hgsu_ + 256 bits, Open Q #1 default). The bootstrap user is seeded from configuration
// (env/secret store in deployment — a committed password is a release-blocker, SP-07) so the first
// login is possible before any account is provisioned through the API. The auth service is a singleton
// over the pooled SQLite context factory, mirroring the other durable-store ports; the same instance is
// exposed as the shared IUserSessionService for the bearer-session middleware.
// ---------------------------------------------------------------------------
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddSingleton<LocalAuthService>();
builder.Services.AddSingleton<IUserSessionService>(sp => sp.GetRequiredService<LocalAuthService>());

// ---------------------------------------------------------------------------
// Authorisation middleware (IDENTITY-SECURITY-5.3, AUTH-FR-03, SP-04): per-conversation/task/artifact
// access control. The shared AuthorizationMiddleware gates the protected local-API routes; the store-backed
// EdgeAuthorizer resolves the authenticated user's participant addresses and checks conversation membership /
// task assignment over the SQLite store. Scoped so each request opens its own short-lived context.
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IResourceAuthorizer, EdgeAuthorizer>();

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

if (app.Environment.IsDevelopment())
{
    app.UseCors("PwaDevelopment");
}

// TLS everywhere (AUTH-FR-04, SP-01): when an HTTPS endpoint is configured the local API redirects
// plain HTTP; without one this is a no-op (a LAN-only PoC can run over http, but production and any
// internet-exposed surface must terminate TLS).
app.UseHttpsRedirection();

// Apply pending EF Core migrations so the schema exists before the Edge begins
// serving the LAN (lifecycle STARTING -> STARTED, product vision §10).
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<EdgeDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.Migrate();

    // Seed the configured bootstrap user (AUTH-FR-02, SP-07: credentials from env/secret store).
    var auth = scope.ServiceProvider.GetRequiredService<LocalAuthService>();
    await auth.SeedBootstrapUserAsync(CancellationToken.None);
}

// Bearer-session authentication (AUTH-FR-02, SP-03): resolves the current user from
// `Authorization: Bearer <token>` for /auth/me and future authorised endpoints.
app.UseSessionAuthentication();

// Authorisation (AUTH-FR-03, SP-04): requires a session on the protected local-API routes and enforces
// per-conversation/task/artifact access for single-resource reads. Runs after session authentication so the
// resolved AuthenticatedUser is available.
app.UseResourceAuthorization();

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
