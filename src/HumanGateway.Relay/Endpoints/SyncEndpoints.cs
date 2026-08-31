using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Api;
using HumanGateway.Relay.Services;

namespace HumanGateway.Relay.Endpoints;

/// <summary>
/// The Relay's sync endpoints (RELAY-FR-02, SYNC-FR-01..07): <c>POST /sync/push</c> applies a gateway's PUSH
/// batch and returns its new push cursor; <c>POST /sync/pull</c> returns the gateway's inbound PULL batch
/// after its echoed cursor. Handlers stay thin — all cursor/idempotency/routing logic lives in
/// <see cref="RelaySyncService"/>, and exceptions translate to <see cref="ProtocolError"/> responses by the
/// global exception handler (SP-07). The Edge always dials out to these endpoints; the Relay never dials in
/// (SP-01).
/// </summary>
public static class SyncEndpoints
{
    /// <summary>Maps the sync endpoint group onto the app.</summary>
    public static void MapSyncEndpoints(this WebApplication app)
    {
        app.MapPost("/sync/push", PushAsync);
        app.MapPost("/sync/pull", PullAsync);
        app.MapArtifactEndpoints();
    }

    /// <summary>
    /// The artifact byte channel (ARTF-FR-01/02, PROTO-FR-04 exception): dedup state, offset-addressed
    /// resumable upload, hash-verified completion, and streaming download — all gated on a REGISTERED gateway
    /// (SP-02). The gateway identity travels as the <c>gatewayId</c> query parameter (body for the state
    /// check). Handlers stay thin; <see cref="RelayArtifactService"/> owns the domain logic.
    /// </summary>
    private static void MapArtifactEndpoints(this WebApplication app)
    {
        // Dedup check — which of the hashes the gateway references does the Relay already hold (NF-03)?
        app.MapPost("/sync/artifacts/state", static async (ArtifactStateRequest request, RelayArtifactService service, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Validate();

            var present = await service.CheckHashesAsync(request.GatewayId, request.Hashes, ct);
            return Results.Ok(new ArtifactStateResponse { Present = present });
        });

        // Resume state — how many bytes the Relay durably holds for the hash, and whether it is complete.
        app.MapGet("/sync/artifacts/{hash}/offset", static async (string hash, string? gatewayId, RelayArtifactService service, CancellationToken ct) =>
        {
            var require = RequireGatewayId(gatewayId);
            if (require is not null)
            {
                return require;
            }

            return Results.Ok(await service.GetOffsetAsync(gatewayId!, hash, ct));
        });

        // One chunk at an explicit offset (idempotent per offset; 409 on mismatch for resumable re-sync).
        app.MapPut("/sync/artifacts/{hash}", static async (string hash, long? offset, string? gatewayId, HttpRequest request, RelayArtifactService service, CancellationToken ct) =>
        {
            var require = RequireGatewayId(gatewayId);
            if (require is not null)
            {
                return require;
            }

            if (offset is null)
            {
                return ApiErrors.BadRequest(ErrorCodes.BadRequest, "The 'offset' query parameter is required.");
            }

            var result = await service.UploadChunkAsync(gatewayId!, hash, offset.Value, request.Body, ct);
            return Results.Ok(result);
        });

        // Finalise: content-hash verification + quota + durable publish (dedup-aware).
        app.MapPost("/sync/artifacts/{hash}/complete", static async (string hash, string? gatewayId, RelayArtifactService service, CancellationToken ct) =>
        {
            var require = RequireGatewayId(gatewayId);
            if (require is not null)
            {
                return require;
            }

            return Results.Ok(await service.CompleteAsync(gatewayId!, hash, ct));
        });

        // Streaming download (Range-capable for resumable downloads, ARTF-FR-02); 404 when absent.
        app.MapGet("/sync/artifacts/{hash}", static async (string hash, string? gatewayId, HttpRequest request, RelayArtifactService service, CancellationToken ct) =>
        {
            var require = RequireGatewayId(gatewayId);
            if (require is not null)
            {
                return require;
            }

            // The BYTEA read stream is non-seekable (true streaming reads), so ASP.NET's built-in range
            // processing cannot serve a Range request — honour the Range header manually (ARTF-FR-02):
            // compute the requested span against the stored size, position the sequential stream past the
            // start offset, and reply 206 Partial Content with the Content-Range header.
            if (System.Net.Http.Headers.RangeHeaderValue.TryParse(request.Headers.Range.ToString(), out var range)
                && range.Ranges.Count == 1)
            {
                var total = await service.GetStoredSizeAsync(gatewayId!, hash, ct);
                if (total is { } size && size > 0)
                {
                    var span = range.Ranges.First();
                    var start = span.From ?? Math.Max(0, size - (span.To ?? 0));
                    if (start >= size)
                    {
                        return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);
                    }

                    var end = span.To is { } to ? Math.Min(to, size - 1) : size - 1;
                    if (end < start)
                    {
                        end = start;
                    }

                    var content = await service.DownloadAsync(gatewayId!, hash, ct);
                    if (content is null)
                    {
                        return ApiErrors.NotFound($"Artifact content '{hash}' is not stored.");
                    }

                    await SkipAsync(content, start, ct).ConfigureAwait(false);
                    return new PartialContentResult(content, start, end - start + 1, size);
                }
            }

            var full = await service.DownloadAsync(gatewayId!, hash, ct);
            return full is null
                ? ApiErrors.NotFound($"Artifact content '{hash}' is not stored.")
                : Results.Stream(full);
        });
    }

    /// <summary>Advances a sequential (non-seekable) stream past <paramref name="count"/> bytes.</summary>
    private static async Task SkipAsync(Stream stream, long count, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        while (count > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(count, buffer.Length)), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            count -= read;
        }
    }

    /// <summary>Rejects an artifact-byte request that omits the gateway identity (SP-02).</summary>
    private static IResult? RequireGatewayId(string? gatewayId)
        => string.IsNullOrWhiteSpace(gatewayId)
            ? ApiErrors.BadRequest(ErrorCodes.BadRequest,
                "The 'gatewayId' query parameter is required (the registered gateway performing the transfer).")
            : null;

    /// <summary>
    /// Applies a gateway's PUSH batch (syncbatch.schema.json) and responds with the result batch carrying the
    /// new push cursor. The request body is the canonical wire SyncBatch; the response is a keepalive result
    /// batch (empty items) whose <c>cursor</c> is the durable acknowledgement the Edge flushes its outbox on.
    /// </summary>
    private static async Task<IResult> PushAsync(RelaySyncService service, SyncBatch batch, CancellationToken ct)
    {
        var response = await service.PushAsync(batch, ct);
        return Results.Ok(response);
    }

    /// <summary>
    /// Returns the gateway's inbound PULL batch (items addressed to it plus the new pull cursor). An empty
    /// items array is a valid keepalive — there is nothing new after the echoed cursor (SYNC-FR-03).
    /// </summary>
    private static async Task<IResult> PullAsync(SyncPullRequest request, RelaySyncService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var response = await service.PullAsync(request.GatewayId, request.SinceCursor, ct);
        return Results.Ok(response);
    }
}
