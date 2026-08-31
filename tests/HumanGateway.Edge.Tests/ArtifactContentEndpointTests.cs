using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HumanGateway.Core.Hashing;
using HumanGateway.Edge.Api;
using HumanGateway.Protocol.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HumanGateway.Edge.Tests;

/// <summary>
/// HTTP-level tests for the Edge artifact byte endpoints (ARTF-FR-01/03, artifacts §6): PUT /artifacts/{id}/content
/// (size-limit + quota + content-hash verification + dedup) and GET /artifacts/{id}/content (download with the
/// artifact's MIME type, hash intact). Covers the acceptance scenarios: upload → download intact, duplicate
/// upload deduplicated, over-limit and over-quota rejected with clear ProtocolError-shaped messages, and a
/// hash-mismatched upload rejected without leaving bytes (SP-06).
/// </summary>
public sealed class ArtifactContentEndpointTests : IClassFixture<ArtifactContentEndpointTests.Factory>
{
    private readonly Factory _factory;

    public ArtifactContentEndpointTests(Factory factory) => _factory = factory;

    // -----------------------------------------------------------------------------------------------
    // Happy path: upload → download, hash intact (artifacts §6 #1)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Upload_ThenDownload_ReturnsTheExactBytes_WithMimeType()
    {
        using var client = _factory.CreateClient();
        var content = Encoding.UTF8.GetBytes("teacher-photo-evidence");
        var (id, _) = await RegisterAsync(client, content, "photo.jpg", "image/jpeg");

        var upload = await client.PutAsync($"/artifacts/{id}/content", new ByteArrayContent(content));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var uploadDoc = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        Assert.True(uploadDoc.RootElement.GetProperty("stored").GetBoolean());

        var download = await client.GetAsync($"/artifacts/{id}/content");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/jpeg", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());

        // The status endpoint reports the bytes are present.
        var status = await client.GetFromJsonAsync<ArtifactContentStatus>($"/artifacts/{id}/content/status", Factory.ApiJson);
        Assert.NotNull(status);
        Assert.True(status.Present);
        Assert.Equal(content.Length, status.StoredBytes);
        Assert.Equal(ArtifactLimitsDefault(), status.MaxSizeBytes);
    }

    // -----------------------------------------------------------------------------------------------
    // Dedup (ARTF-FR-01, artifacts §6 #2)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Upload_SameContentTwice_IsDeduplicated_NoSecondWrite()
    {
        using var client = _factory.CreateClient();
        var content = Encoding.UTF8.GetBytes("duplicate-evidence");
        var (id1, _) = await RegisterAsync(client, content);
        var (id2, _) = await RegisterAsync(client, content); // different id, identical bytes

        var first = await client.PutAsync($"/artifacts/{id1}/content", new ByteArrayContent(content));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // The second upload (same hash, different id) reports Stored=false — identical bytes already on disk.
        var second = await client.PutAsync($"/artifacts/{id2}/content", new ByteArrayContent(content));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var secondDoc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.False(secondDoc.RootElement.GetProperty("stored").GetBoolean());
    }

    // -----------------------------------------------------------------------------------------------
    // Limits and quota (ARTF-FR-03, artifacts §6 #4)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Upload_OverSizeLimit_IsRejectedWithSizeExceeded()
    {
        // Small limit so the test upload trips it without transferring megabytes. Registration accepts a tiny
        // artifact; the upload itself declares a larger body than the gateway permits (ARTF-FR-03).
        using var limited = _factory.WithArtifactOptions(maxSizeBytes: 64, quotaBytes: 4096);
        using var client = limited.CreateClient();

        var tiny = new byte[4];
        var (id, _) = await RegisterAsync(client, tiny);

        var oversized = new byte[128];
        Random.Shared.NextBytes(oversized);
        var response = await client.PutAsync($"/artifacts/{id}/content", new ByteArrayContent(oversized));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(Factory.ApiJson);
        Assert.Equal(ErrorCodes.SizeExceeded, error?.Code);
        Assert.False(error?.Retryable);
    }

    [Fact]
    public async Task Register_OverSizeLimit_IsRejectedWithSizeExceeded()
    {
        using var limited = _factory.WithArtifactOptions(maxSizeBytes: 64, quotaBytes: 4096);
        using var client = limited.CreateClient();

        var response = await client.PostAsJsonAsync("/artifacts", new
        {
            hash = "sha256:" + new string('e', 64),
            sizeBytes = 128,
            mimeType = "application/octet-stream",
            filename = "big.bin",
        }, Factory.ApiJson);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(Factory.ApiJson);
        Assert.Equal(ErrorCodes.SizeExceeded, error?.Code);
    }

    [Fact]
    public async Task Upload_OverQuota_IsRejectedWithQuotaExceeded()
    {
        // Quota smaller than the content so the first upload trips it on a fresh store (self-contained:
        // the gateway enforces quota against the bytes it actually stores, ARTF-FR-03).
        using var limited = _factory.WithArtifactOptions(maxSizeBytes: 4096, quotaBytes: 10);
        using var client = limited.CreateClient();
        var content = Encoding.UTF8.GetBytes("quota-consumer"); // 13 bytes > 10-byte quota
        var (id, _) = await RegisterAsync(client, content);

        var response = await client.PutAsync($"/artifacts/{id}/content", new ByteArrayContent(content));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(Factory.ApiJson);
        Assert.Equal(ErrorCodes.QuotaExceeded, error?.Code);
    }

    [Fact]
    public async Task Upload_QuotaNotDoubleCounted_ForDeduplicatedContent()
    {
        using var limited = _factory.WithArtifactOptions(maxSizeBytes: 4096, quotaBytes: 100);
        using var client = limited.CreateClient();
        var content = Encoding.UTF8.GetBytes("dedup-against-quota");
        var (id1, _) = await RegisterAsync(client, content);
        var (id2, _) = await RegisterAsync(client, content);

        // First write consumes the quota; the deduplicated second write is free (identical bytes, ARTF-FR-01).
        var first = await client.PutAsync($"/artifacts/{id1}/content", new ByteArrayContent(content));
        if (first.StatusCode != HttpStatusCode.Created)
        {
            var body = await first.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException($"First upload failed: {first.StatusCode} body={body}");
        }

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await client.PutAsync($"/artifacts/{id2}/content", new ByteArrayContent(content));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    // -----------------------------------------------------------------------------------------------
    // Integrity (SP-06)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Upload_HashMismatch_IsRejected_AndNoBytesAreServed()
    {
        using var client = _factory.CreateClient();
        var registered = Encoding.UTF8.GetBytes("declared-content");
        var (id, _) = await RegisterAsync(client, registered);

        // Upload different bytes than the registered hash declares.
        var tampered = Encoding.UTF8.GetBytes("tampered-content");
        var response = await client.PutAsync($"/artifacts/{id}/content", new ByteArrayContent(tampered));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(Factory.ApiJson);
        Assert.Equal(ErrorCodes.HashMismatch, error?.Code);

        // Nothing was served at the content path (SP-06: a tampered upload never leaves a servable artifact).
        var download = await client.GetAsync($"/artifacts/{id}/content");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    [Fact]
    public async Task Upload_UnknownArtifactId_IsNotFound()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsync("/artifacts/artifact:ghost/content",
            new ByteArrayContent(Encoding.UTF8.GetBytes("ghost")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_BeforeBytesUploaded_IsNotFound()
    {
        using var client = _factory.CreateClient();
        var content = Encoding.UTF8.GetBytes("metadata-only");
        var (id, _) = await RegisterAsync(client, content);

        var download = await client.GetAsync($"/artifacts/{id}/content");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    private static async Task<(string Id, string Hash)> RegisterAsync(
        HttpClient client, byte[] content, string filename = "evidence.bin", string mimeType = "application/octet-stream")
    {
        var hash = ContentHasher.Compute(content);
        var response = await client.PostAsJsonAsync("/artifacts", new
        {
            hash,
            sizeBytes = content.Length,
            mimeType,
            filename,
        }, Factory.ApiJson);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("id").GetString()!;
        return (id, hash);
    }

    /// <summary>The default 50 MiB limit from ArtifactStoreOptions (the shared Core default).</summary>
    private static long ArtifactLimitsDefault() => HumanGateway.Core.Artifacts.ArtifactLimits.DefaultMaxArtifactSizeBytes;

    /// <summary>
    /// Hosts the Edge over a unique temp SQLite database with configurable artifact options. Shares the
    /// WebApplicationFactory pattern from <see cref="LocalApiEndpointTests.Factory"/>.
    /// </summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        public static readonly JsonSerializerOptions ApiJson = CreateApiJson();

        private readonly string _dir = Path.Combine(Path.GetTempPath(), "hgedge-art-" + Guid.NewGuid().ToString("N"));

        public Factory() => Directory.CreateDirectory(_dir);

        /// <summary>A factory clone with custom artifact limits (size limit, quota).</summary>
        public Factory WithArtifactOptions(long? maxSizeBytes = null, long? quotaBytes = null)
        {
            var clone = new Factory(maxSizeBytes, quotaBytes);
            return clone;
        }

        private readonly long? _maxSizeBytes;
        private readonly long? _quotaBytes;

        private Factory(long? maxSizeBytes, long? quotaBytes)
        {
            _maxSizeBytes = maxSizeBytes;
            _quotaBytes = quotaBytes;
            Directory.CreateDirectory(_dir);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_dir, "edge.db"),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString();

            // UseSetting writes a HOST setting, which is available to builder.Configuration from the very start
            // of Program.cs. A plain ConfigureAppConfiguration override is applied only at builder.Build(), so
            // Program.cs's `GetConnectionString("Edge")` (read mid-construction) would silently fall back to the
            // shared repo default database instead of this test's temp file — same precedence trap as the Relay
            // factory (CLOUD-RELAY-4.3).
            builder.UseSetting("ConnectionStrings:Edge", connectionString);

            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["Artifacts:RootPath"] = Path.Combine(_dir, "artifacts"),
                };
                if (_maxSizeBytes is { } max)
                {
                    settings["Artifacts:MaxArtifactSizeBytes"] = max.ToString();
                }

                if (_quotaBytes is { } quota)
                {
                    settings["Artifacts:QuotaBytes"] = quota.ToString();
                }

                config.AddInMemoryCollection(settings);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try
            {
                if (Directory.Exists(_dir))
                {
                    Directory.Delete(_dir, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of temp files; a leaked temp dir is harmless.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup (Windows file-lock window).
            }
        }

        private static JsonSerializerOptions CreateApiJson()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = false,
            };
            return options;
        }
    }
}
