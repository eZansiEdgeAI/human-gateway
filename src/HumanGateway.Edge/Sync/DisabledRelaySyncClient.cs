using HumanGateway.Protocol.Models;

namespace HumanGateway.Edge.Sync;

/// <summary>
/// Placeholder <see cref="IRelaySyncClient"/> used until the synchronisation feature provides the real HTTPS
/// transport. <see cref="IsConfigured"/> is false, so the worker never touches the network: the Edge stays
/// fully functional on the LAN and the durable outbox retains every entry for later sync (local-edge §7 #4).
/// </summary>
public sealed class DisabledRelaySyncClient : IRelaySyncClient
{
    /// <inheritdoc />
    public bool IsConfigured => false;

    /// <inheritdoc />
    /// <remarks>Never called — the worker guards on <see cref="IsConfigured"/> first.</remarks>
    public Task<PushResult> PushAsync(SyncBatch batch, CancellationToken ct = default)
        => throw new InvalidOperationException("No Relay is configured; PushAsync must not be called.");

    /// <inheritdoc />
    /// <remarks>Never called — the worker guards on <see cref="IsConfigured"/> first.</remarks>
    public Task<SyncBatch?> PullAsync(string? sinceCursor, CancellationToken ct = default)
        => throw new InvalidOperationException("No Relay is configured; PullAsync must not be called.");
}
