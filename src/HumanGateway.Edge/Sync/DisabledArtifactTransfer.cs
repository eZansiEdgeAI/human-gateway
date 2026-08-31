using HumanGateway.Core.Artifacts;

namespace HumanGateway.Edge.Sync;

/// <summary>
/// Placeholder <see cref="IArtifactTransfer"/> used until a Relay is configured (<see cref="RelayArtifactOptions.Enabled"/>
/// is false). The Edge stays fully functional offline-first: inbound artifact references simply await a later
/// sync cycle once the channel is configured (NF-01).
/// </summary>
public sealed class DisabledArtifactTransfer : IArtifactTransfer
{
    /// <inheritdoc />
    public bool IsConfigured => false;

    /// <inheritdoc />
    /// <remarks>Never called — the worker guards on <see cref="IsConfigured"/> first.</remarks>
    public Task<IReadOnlyList<string>> CheckHashesAsync(IReadOnlyCollection<string> hashes, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    /// <inheritdoc />
    /// <remarks>Never called — the worker guards on <see cref="IsConfigured"/> first.</remarks>
    public Task UploadAsync(string hash, long sizeBytes, Stream content, CancellationToken ct = default)
        => throw new InvalidOperationException("No Relay is configured; UploadAsync must not be called.");

    /// <inheritdoc />
    /// <remarks>Never called — the worker guards on <see cref="IsConfigured"/> first.</remarks>
    public Task<long?> GetRemoteSizeAsync(string hash, CancellationToken ct = default)
        => Task.FromResult<long?>(null);

    /// <inheritdoc />
    /// <remarks>Never called — the worker guards on <see cref="IsConfigured"/> first.</remarks>
    public Task<long> DownloadAsync(string hash, Stream sink, CancellationToken ct = default)
        => throw new InvalidOperationException("No Relay is configured; DownloadAsync must not be called.");
}
