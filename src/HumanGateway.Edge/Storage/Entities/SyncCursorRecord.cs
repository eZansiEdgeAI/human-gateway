namespace HumanGateway.Edge.Storage.Entities;

/// <summary>
/// Durable per-gateway sync-cursor state (SYNC-FR-03): the Relay-issued push cursor, the gateway's own pull
/// cursor, and the in-flight push batch identity used to keep retries idempotent (SYNC-FR-02). Cursors are
/// opaque tokens — the worker stores and echoes them without interpreting them. One row per gateway.
/// </summary>
public sealed class SyncCursorRecord
{
    /// <summary>The gateway whose sync state this is — the primary key.</summary>
    public string GatewayId { get; set; } = null!;

    /// <summary>The Relay's push cursor (ack watermark), echoed as <c>sinceCursor</c> on the next push.</summary>
    public string? PushCursor { get; set; }

    /// <summary>The gateway's own pull cursor, echoed as <c>sinceCursor</c> on the next pull.</summary>
    public string? PullCursor { get; set; }

    /// <summary>The in-flight push batch identity to reuse on retry (null when nothing is in flight).</summary>
    public string? InFlightBatchId { get; set; }

    /// <summary>The in-flight push batch idempotency key to reuse on retry.</summary>
    public string? InFlightIdempotencyKey { get; set; }

    /// <summary>The sequence watermark the in-flight push batch advanced to (null when nothing is in flight).</summary>
    public long? InFlightAfterSequence { get; set; }
}
