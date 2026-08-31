using Microsoft.AspNetCore.Http;

namespace HumanGateway.Relay.Api;

/// <summary>
/// A streaming 206 Partial Content response for the artifact download endpoint (ARTF-FR-02 — resumable
/// downloads over low bandwidth). ASP.NET's built-in range processing requires a <em>seekable</em> stream, but
/// the BYTEA read stream is deliberately non-seekable (true streaming reads, never materialised in memory), so
/// the Relay honours the <c>Range</c> header manually: the endpoint positions the stream past the requested
/// offset and this result writes exactly the requested byte span with the <c>Content-Range</c> header.
/// </summary>
public sealed class PartialContentResult : IResult
{
    private readonly Stream _content;
    private readonly long _start;
    private readonly long _length;
    private readonly long _total;

    /// <summary>Creates the result over a stream positioned at <paramref name="start"/>.</summary>
    public PartialContentResult(Stream content, long start, long length, long total)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _start = start;
        _length = length;
        _total = total;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status206PartialContent;
        response.ContentType = "application/octet-stream";
        response.Headers.ContentRange = $"bytes {_start}-{_start + _length - 1}/{_total}";
        response.Headers.AcceptRanges = "bytes";

        var buffer = new byte[64 * 1024];
        long remaining = _length;
        while (remaining > 0)
        {
            var read = await _content
                .ReadAsync(buffer.AsMemory(0, (int)Math.Min(remaining, buffer.Length)), httpContext.RequestAborted)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await response.Body.WriteAsync(buffer.AsMemory(0, read), httpContext.RequestAborted).ConfigureAwait(false);
            remaining -= read;
        }
    }
}
