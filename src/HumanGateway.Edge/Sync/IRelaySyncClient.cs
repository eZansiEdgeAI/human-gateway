using HumanGateway.Protocol.Models;

namespace HumanGateway.Edge.Sync;

/// <summary>
/// The Edge Gateway's outbound sync hook to the Cloud Relay (EDGE-FR-05, SP-01). The worker dials out to push
/// its outbox batches and pull inbound batches; the Edge never accepts inbound sync connections. The concrete
/// HTTPS transport (registration, TLS, wire framing) is owned by the synchronisation feature — this interface
/// is the seam the worker drives today, so the full protocol can drop in without changing the worker loop.
/// </summary>
public interface IRelaySyncClient
{
    /// <summary>
    /// True when a Relay is configured (base URL + identity present). When false the worker skips the network
    /// entirely and simply retains outbox entries for later sync (offline-first, NF-01).
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>Sends a PUSH batch to the Relay and returns its acknowledged cursor (SYNC-FR-03).</summary>
    Task<PushResult> PushAsync(SyncBatch batch, CancellationToken ct = default);

    /// <summary>
    /// Requests inbound batches from the Relay after the local pull cursor, or returns <see langword="null"/>
    /// when there is nothing new (SYNC-FR-03).
    /// </summary>
    Task<SyncBatch?> PullAsync(string? sinceCursor, CancellationToken ct = default);
}

/// <summary>The Relay's response to a push batch.</summary>
public sealed record PushResult
{
    /// <summary>The Relay's new cursor (opaque); null at the start position. Echoed back on the next push.</summary>
    public string? Cursor { get; init; }
}
