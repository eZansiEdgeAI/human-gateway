namespace HumanGateway.Core.Cursor;

/// <summary>
/// Durable port for the sync worker's per-gateway cursor state (SYNC-FR-03): the Relay-issued push cursor,
/// the gateway's own pull cursor, and the in-flight push batch identity used to keep retries idempotent
/// (SYNC-FR-02). Cursors are <em>opaque</em> to the worker — it stores and echoes them, never interprets
/// them. The SQLite implementation is owned by the edge-engineer; the in-memory reference implementation
/// (<see cref="InMemorySyncCursorStore"/>) is used by tests and simple deployments.
/// </summary>
/// <remarks>
/// Durability matters here: a reconnect resumes from the last cursor rather than re-pulling everything
/// (NF-02, SYNC-FR-03), and a retried push reuses the same <c>batchId</c> + <c>idempotencyKey</c> so the
/// receiver collapses it as a replay (SYNC-FR-02, NF-05). The worker persists this state before and after
/// each exchange, so a crash mid-push or a lost acknowledgement never duplicates delivery.
/// </remarks>
public interface ISyncCursorStore
{
    /// <summary>
    /// Returns the stored cursor state for a gateway, or an empty <see cref="SyncCursorState"/> (start
    /// position) when nothing has been saved yet.
    /// </summary>
    Task<SyncCursorState> GetAsync(string gatewayId, CancellationToken ct = default);

    /// <summary>Durably saves (upserts) the cursor state for a gateway.</summary>
    Task SaveAsync(SyncCursorState state, CancellationToken ct = default);
}

/// <summary>
/// The sync worker's durable per-gateway cursor + in-flight push state. Immutable (records are copied with
/// <c>with</c> expressions); a <see langword="null"/> cursor means "start position / first exchange".
/// </summary>
public sealed record SyncCursorState
{
    /// <summary>The gateway whose sync state this is.</summary>
    public string GatewayId { get; init; } = null!;

    /// <summary>The Relay's push cursor (ack watermark), echoed as <c>sinceCursor</c> on the next push.</summary>
    public string? PushCursor { get; init; }

    /// <summary>The gateway's own pull cursor, echoed as <c>sinceCursor</c> on the next pull.</summary>
    public string? PullCursor { get; init; }

    /// <summary>The in-flight push batch identity to reuse on retry (null when nothing is in flight).</summary>
    public string? InFlightBatchId { get; init; }

    /// <summary>The in-flight push batch idempotency key to reuse on retry.</summary>
    public string? InFlightIdempotencyKey { get; init; }

    /// <summary>The sequence watermark the in-flight push batch advanced to (null when nothing is in flight).</summary>
    public long? InFlightAfterSequence { get; init; }

    /// <summary>The "nothing stored yet" state for a gateway (start position, no in-flight batch).</summary>
    public static SyncCursorState Empty(string gatewayId) => new() { GatewayId = gatewayId };
}
