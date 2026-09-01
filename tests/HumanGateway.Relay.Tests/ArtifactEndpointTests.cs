using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HumanGateway.Core.Hashing;
using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Api;
using HumanGateway.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HumanGateway.Relay.Tests;

/// <summary>
/// HTTP-level tests for the Relay artifact byte channel (RELAY-FR-01, ARTF-FR-01/02/03, artifacts §6): the
/// dedup state check, the offset-addressed resumable upload, the hash-verified completion, and the streaming
/// (Range-capable) download. Boots the real Relay <c>Program</c> over Testcontainers PostgreSQL and proves the
/// acceptance scenarios: upload → download intact with the hash verified, an interrupted upload resuming from
/// its accepted offset, duplicate content deduplicated (no second store row), unsigned/unregistered gateways
/// rejected (AUTH-FR-04, SP-02), and over-limit uploads rejected with a clear error. Requests are signed exactly
/// as the production Edge signs them.
/// </summary>
public sealed class ArtifactEndpointTests : IClassFixture<PostgresRelayFixture>
{
    private static readonly JsonSerializerOptions ApiJson = CreateApiJson();

    private readonly PostgresRelayFixture _fixture;

    /// <summary>Signing ring shared by every client a test creates (registration stores the derived keys).</summary>
    private readonly TestSigningHandler _signing = new();

    public ArtifactEndpointTests(PostgresRelayFixture fixture) => _fixture = fixture;

    /// <summary>Creates a client whose outbound requests are signed via the shared test signing ring.</summary>
    private (HttpClient Client, TestSigningHandler Signing) CreateClient(WebApplicationFactory<Program> factory)
        => (factory.CreateDefaultClient(_signing), _signing);

    // -----------------------------------------------------------------------------------------------
    // Identity gate (AUTH-FR-04, SP-02)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task StateCheck_UnregisteredGateway_IsRejected()
    {
        using var factory = new RelayApiFactory(_fixture);
        var (client, _) = CreateClient(factory);

        // Never registered → the authentication middleware rejects the unknown identity (SP-02).
        var response = await client.PostAsJsonAsync("/sync/artifacts/state", new
        {
            gatewayId = "gateway:ghost-artifacts",
            hashes = new[] { HashOf("ghost") },
        }, ApiJson);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.NotFound, error?.Code);
    }

    // -----------------------------------------------------------------------------------------------
    // Upload → download, hash intact (artifacts §6 #1)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Upload_ThenDownload_ReturnsTheExactBytes()
    {
        using var factory = new RelayApiFactory(_fixture);
        var (client, signing) = CreateClient(factory);
        var gatewayId = await RegisterGatewayAsync(client, signing, UniqueGatewayId("gateway:art-a"));

        var content = Encoding.UTF8.GetBytes("teacher-photo-evidence-from-edge");
        var hash = HashOf(content);

        // Dedup check first: the Relay does not hold it yet.
        var state = await PostStateAsync(client, gatewayId, hash);
        Assert.Empty(state.Present);

        // Upload (single chunk at offset 0) and finalise.
        var chunk = await PutChunkAsync(client, gatewayId, hash, 0, content);
        Assert.Equal(content.Length, chunk.Received);
        Assert.False(chunk.Complete);
        var complete = await PostCompleteAsync(client, gatewayId, hash);
        Assert.True(complete.Stored);

        // The offset state now reports complete with the full size.
        var offset = await GetOffsetAsync(client, gatewayId, hash);
        Assert.True(offset.Complete);
        Assert.Equal(content.Length, offset.Received);

        // Download returns the exact bytes, hash intact (SP-06).
        var download = await client.GetAsync(DownloadUrl(gatewayId, hash));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());

        // The dedup check now reports the hash present.
        var after = await PostStateAsync(client, gatewayId, hash);
        Assert.Contains(hash, after.Present);
    }

    // -----------------------------------------------------------------------------------------------
    // Resumable upload (ARTF-FR-02, artifacts §6 #3)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Upload_InterruptedMidWay_ResumesFromTheAcceptedOffset_AndCompletes()
    {
        using var factory = new RelayApiFactory(_fixture);
        var (client, signing) = CreateClient(factory);
        var gatewayId = await RegisterGatewayAsync(client, signing, UniqueGatewayId("gateway:art-b"));

        var content = Encoding.UTF8.GetBytes("a-large-evidence-file-that-is-split-into-two-chunks");
        var hash = HashOf(content);
        var firstChunk = content.AsSpan(0, content.Length / 2).ToArray();
        var secondChunk = content.AsSpan(content.Length / 2).ToArray();

        // First attempt: only the first chunk arrives before the transfer dies.
        var accepted = await PutChunkAsync(client, gatewayId, hash, 0, firstChunk);
        Assert.Equal(firstChunk.Length, accepted.Received);

        // The retry queries the resume offset and continues from exactly where the receiver is.
        var resume = await GetOffsetAsync(client, gatewayId, hash);
        Assert.Equal(firstChunk.Length, resume.Received);
        Assert.False(resume.Complete);

        var continued = await PutChunkAsync(client, gatewayId, hash, resume.Received, secondChunk);
        Assert.Equal(content.Length, continued.Received);

        await PostCompleteAsync(client, gatewayId, hash);

        var download = await client.GetAsync(DownloadUrl(gatewayId, hash));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_ChunkAtWrongOffset_IsRejectedWithConflict()
    {
        using var factory = new RelayApiFactory(_fixture);
        var (client, signing) = CreateClient(factory);
        var gatewayId = await RegisterGatewayAsync(client, signing, UniqueGatewayId("gateway:art-c"));

        var content = Encoding.UTF8.GetBytes("offset-mismatch-evidence");
        var hash = HashOf(content);

        // Nothing received yet — a chunk claiming offset 5 must be rejected with the actual position.
        var response = await client.PutAsync(
            $"/sync/artifacts/{hash}?offset=5&gatewayId={gatewayId}",
            new ByteArrayContent(content));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.Conflict, error?.Code);
        Assert.True(error?.Retryable);
    }

    // -----------------------------------------------------------------------------------------------
    // Dedup (ARTF-FR-01, artifacts §6 #2)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Upload_IdenticalContentTwice_IsDeduplicated_NoSecondBlobRow()
    {
        using var factory = new RelayApiFactory(_fixture);
        var (client, signing) = CreateClient(factory);
        var gatewayId = await RegisterGatewayAsync(client, signing, UniqueGatewayId("gateway:art-d"));

        var content = Encoding.UTF8.GetBytes("deduplicated-evidence");
        var hash = HashOf(content);

        await PutChunkAsync(client, gatewayId, hash, 0, content);
        var first = await PostCompleteAsync(client, gatewayId, hash);
        Assert.True(first.Stored);

        // A second gateway uploading the same bytes deduplicates: Stored=false, no new blob row.
        var secondGateway = await RegisterGatewayAsync(client, signing, UniqueGatewayId("gateway:art-e"));
        var offset = await GetOffsetAsync(client, secondGateway, hash);
        Assert.True(offset.Complete);
        var complete = await PostCompleteAsync(client, secondGateway, hash);
        Assert.False(complete.Stored);
    }

    // -----------------------------------------------------------------------------------------------
    // Integrity and limits (SP-06, ARTF-FR-03)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Complete_HashMismatch_IsRejectedWithHashMismatch()
    {
        using var factory = new RelayApiFactory(_fixture);
        var (client, signing) = CreateClient(factory);
        var gatewayId = await RegisterGatewayAsync(client, signing, UniqueGatewayId("gateway:art-f"));

        var content = Encoding.UTF8.GetBytes("declared-hash-is-this-file");
        var hash = HashOf("something-else"); // declared hash does not match the uploaded bytes

        await PutChunkAsync(client, gatewayId, hash, 0, content);

        var response = await client.PostAsync(
            $"/sync/artifacts/{hash}/complete?gatewayId={gatewayId}", content: null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.HashMismatch, error?.Code);
    }

    [Fact]
    public async Task Upload_OverSizeLimit_IsRejectedWithSizeExceeded()
    {
        using var factory = new LimitedArtifactRelayFactory(_fixture);
        var (client, signing) = CreateClient(factory);
        var gatewayId = await RegisterGatewayAsync(client, signing, UniqueGatewayId("gateway:art-g"));

        // 80 bytes — clearly over the limited factory's 64-byte per-artifact ceiling.
        var content = Encoding.UTF8.GetBytes(new string('x', 80));
        Assert.True(content.Length > 64);
        var hash = HashOf(content);

        var response = await client.PutAsync(
            $"/sync/artifacts/{hash}?offset=0&gatewayId={gatewayId}",
            new ByteArrayContent(content));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.SizeExceeded, error?.Code);
    }

    // -----------------------------------------------------------------------------------------------
    // Range download (resumable downloads, ARTF-FR-02)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Download_WithRangeHeader_ReturnsPartialContent()
    {
        using var factory = new RelayApiFactory(_fixture);
        var (client, signing) = CreateClient(factory);
        var gatewayId = await RegisterGatewayAsync(client, signing, UniqueGatewayId("gateway:art-h"));

        var content = Encoding.UTF8.GetBytes("range-downloadable-evidence");
        var hash = HashOf(content);
        await PutChunkAsync(client, gatewayId, hash, 0, content);
        await PostCompleteAsync(client, gatewayId, hash);

        var request = new HttpRequestMessage(HttpMethod.Get, DownloadUrl(gatewayId, hash));
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(6, null);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(content[6..], await response.Content.ReadAsByteArrayAsync());
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    private static string HashOf(byte[] content) => ContentHasher.Compute(content);

    private static string HashOf(string content) => ContentHasher.Compute(Encoding.UTF8.GetBytes(content));

    private static async Task<ArtifactStateResponse> PostStateAsync(HttpClient client, string gatewayId, string hash)
    {
        var response = await client.PostAsJsonAsync("/sync/artifacts/state",
            new { gatewayId, hashes = new[] { hash } }, ApiJson);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ArtifactStateResponse>(ApiJson))!;
    }

    private static async Task<ArtifactChunkResult> PutChunkAsync(
        HttpClient client, string gatewayId, string hash, long offset, byte[] content)
    {
        var response = await client.PutAsync(
            $"/sync/artifacts/{hash}?offset={offset}&gatewayId={gatewayId}",
            new ByteArrayContent(content));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ArtifactChunkResult>(ApiJson))!;
    }

    private static async Task<ArtifactCompleteResult> PostCompleteAsync(HttpClient client, string gatewayId, string hash)
    {
        var response = await client.PostAsync($"/sync/artifacts/{hash}/complete?gatewayId={gatewayId}", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ArtifactCompleteResult>(ApiJson))!;
    }

    private static async Task<ArtifactOffsetState> GetOffsetAsync(HttpClient client, string gatewayId, string hash)
    {
        var response = await client.GetAsync($"/sync/artifacts/{hash}/offset?gatewayId={gatewayId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ArtifactOffsetState>(ApiJson))!;
    }

    private static string DownloadUrl(string gatewayId, string hash)
        => $"/sync/artifacts/{hash}?gatewayId={gatewayId}";

    private static string UniqueGatewayId(string prefix)
        => $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}";

    private static async Task<string> RegisterGatewayAsync(
        HttpClient client, TestSigningHandler signing, string gatewayId)
    {
        var issued = await client.PostAsJsonAsync("/gateways", new { gatewayId }, ApiJson);
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var token = (await issued.Content.ReadFromJsonAsync<RegistrationIssued>(ApiJson))!.RegistrationToken;

        // AUTH-FR-04: register the derived request-signing key so artifact requests sign as this gateway.
        signing.Keys[gatewayId] = GatewayRequestSigning.DeriveKey(token);

        var confirm = await client.PostAsJsonAsync($"/gateways/{gatewayId}/register",
            new { gatewayId, registrationToken = token }, ApiJson);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        return gatewayId;
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

    /// <summary>A Relay host with a small per-artifact size limit for limit tests.</summary>
    private sealed class LimitedArtifactRelayFactory : WebApplicationFactory<Program>
    {
        private readonly PostgresRelayFixture _fixture;

        public LimitedArtifactRelayFactory(PostgresRelayFixture fixture) => _fixture = fixture;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Same connection-string precedence trick as RelayApiFactory (UseSetting beats appsettings).
            builder.UseSetting("ConnectionStrings:Relay", _fixture.Container.GetConnectionString());
            builder.UseSetting("Relay:Artifacts:MaxArtifactSizeBytes", "64");
        }
    }
}
