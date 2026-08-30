namespace HumanGateway.Edge.Sync;

/// <summary>
/// Edge Gateway lifecycle states for the background sync worker (product vision §10):
/// <c>STARTING → STARTED (serving LAN) → SYNCING (outbound to Relay) → RECOVERING (reconcile) → STOPPING</c>.
/// On restart the worker re-enters <see cref="Recovering"/> to reconcile the local store with Relay cursors
/// before resuming sync.
/// </summary>
public enum SyncWorkerState
{
    /// <summary>The worker is initialising before its first cycle.</summary>
    Starting,

    /// <summary>Healthy: serving the LAN and, when a Relay is configured, syncing periodically.</summary>
    Started,

    /// <summary>Actively exchanging a push/pull batch with the Relay.</summary>
    Syncing,

    /// <summary>Reconciling local store state with Relay cursors after startup or failure.</summary>
    Recovering,

    /// <summary>Shutdown requested; draining the current cycle before stopping.</summary>
    Stopping,

    /// <summary>Terminal: the loop has exited.</summary>
    Stopped,
}
