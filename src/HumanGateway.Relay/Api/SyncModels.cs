namespace HumanGateway.Relay.Api;

/// <summary>
/// Body of <c>POST /sync/pull</c> — a gateway requests its inbound (Relay → gateway) batch after its echoed
/// pull cursor (SYNC-FR-03). <see cref="SinceCursor"/> is the opaque cursor the Relay issued in the previous
/// PULL response; <see langword="null"/> on the first exchange.
/// </summary>
public sealed record SyncPullRequest
{
    /// <summary>The registered gateway requesting its inbound stream.</summary>
    public string GatewayId { get; init; } = null!;

    /// <summary>Opaque pull cursor from the Relay's last PULL response; null on the first exchange.</summary>
    public string? SinceCursor { get; init; }

    /// <summary>Validates the request shape; throws a 400 on failure.</summary>
    public void Validate()
    {
        var errors = new List<string>();
        if (!RequestValidation.GatewayIdRegex().IsMatch(GatewayId ?? string.Empty))
        {
            errors.Add("gatewayId must be a durable ID (8..128 chars of [A-Za-z0-9._:-]); got "
                + (GatewayId is null ? "null" : $"'{GatewayId}'") + ".");
        }

        if (SinceCursor is { Length: < 1 or > 1024 })
        {
            errors.Add("sinceCursor must be 1..1024 characters (the schema's opaque cursor bound).");
        }

        RequestValidation.ThrowIfInvalid(errors);
    }
}

/// <summary>
/// Body of <c>POST /sync/artifacts/state</c> — a gateway asks which artifact content hashes the Relay already
/// holds so it transfers only the missing bytes (dedup, ARTF-FR-01, NF-03).
/// </summary>
public sealed record ArtifactStateRequest
{
    /// <summary>The registered gateway performing the dedup check.</summary>
    public string GatewayId { get; init; } = null!;

    /// <summary>Content hashes (<c>sha256:&lt;hex&gt;</c>) to check. Duplicates are collapsed.</summary>
    public IReadOnlyList<string> Hashes { get; init; } = Array.Empty<string>();

    /// <summary>Validates the request shape; throws a 400 on failure.</summary>
    public void Validate()
    {
        var errors = new List<string>();
        if (!RequestValidation.GatewayIdRegex().IsMatch(GatewayId ?? string.Empty))
        {
            errors.Add("gatewayId must be a durable ID (8..128 chars of [A-Za-z0-9._:-]); got "
                + (GatewayId is null ? "null" : $"'{GatewayId}'") + ".");
        }

        if (Hashes is null || Hashes.Count == 0)
        {
            errors.Add("hashes must contain at least one content hash.");
        }

        if (Hashes is { Count: > 1000 })
        {
            errors.Add("hashes may contain at most 1000 entries per request.");
        }

        RequestValidation.ThrowIfInvalid(errors);
    }
}

/// <summary>Response of <c>POST /sync/artifacts/state</c>: the hashes the Relay already holds.</summary>
public sealed record ArtifactStateResponse
{
    /// <summary>Content hashes present in the Relay's blob store (dedup: transfer everything not listed).</summary>
    public IReadOnlyList<string> Present { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The receiving side's offset state for a hash: how many bytes it durably holds and whether the content is
/// complete. A complete hash has <c>received == size</c> and never stores partial bytes.
/// </summary>
public sealed record ArtifactOffsetState
{
    /// <summary>Bytes durably accepted for this hash (0 when nothing has been received).</summary>
    public long Received { get; init; }

    /// <summary>True when the content is complete and verified (published to the blob store).</summary>
    public bool Complete { get; init; }
}

/// <summary>Result of accepting one upload chunk.</summary>
public sealed record ArtifactChunkResult
{
    /// <summary>Bytes durably accepted for the hash after this chunk.</summary>
    public long Received { get; init; }

    /// <summary>True when the chunk completed the upload (the caller then finalises with the complete call).</summary>
    public bool Complete { get; init; }
}

/// <summary>Result of finalising an upload (content-hash verified and published, or deduplicated).</summary>
public sealed record ArtifactCompleteResult
{
    /// <summary>True when bytes were newly published; false when identical bytes were already stored (dedup).</summary>
    public bool Stored { get; init; }
}
