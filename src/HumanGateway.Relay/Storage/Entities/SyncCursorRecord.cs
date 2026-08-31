namespace HumanGateway.Relay.Storage.Entities;

/// <summary>
/// Durable per-gateway sync-cursor state at the Relay (SYNC-FR-03). <see cref="PushCursor"/> is the opaque
/// cursor the Relay issues after applying a gateway's PUSH batch — the Edge echoes it back as
/// <c>sinceCursor</c> on the next push. <see cref="PullCursor"/> is the Relay-side cursor for the inbound
/// (Relay → gateway) direction: it marks how far the gateway has pulled, so the Relay returns only newer
/// batches. Cursors are opaque tokens — the Relay stores and echoes them without interpreting them. One row
/// per gateway.
/// </summary>
public sealed class SyncCursorRecord
{
    /// <summary>The gateway whose sync state this is — the primary key.</summary>
    public string GatewayId { get; set; } = null!;

    /// <summary>The cursor covering the gateway's last applied PUSH batch, issued to the Edge.</summary>
    public string? PushCursor { get; set; }

    /// <summary>The cursor covering what the gateway has pulled, updated on each PULL response.</summary>
    public string? PullCursor { get; set; }
}
