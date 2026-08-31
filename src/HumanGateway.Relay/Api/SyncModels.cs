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
