namespace HumanGateway.Edge.Security;

/// <summary>
/// Serialises registration handshake work (AUTH-FR-01, SP-02): at most one request/confirm/rotate attempt runs
/// at a time, so two cycles can never race the Relay (e.g. a sync-worker retry and the registration worker's
/// expiry check both calling <see cref="GatewayIdentityManager"/> concurrently). Cheap and re-entrant-safe:
/// callers await <see cref="RunAsync"/> and the delegate runs inside a <see cref="SemaphoreSlim"/> gate.
/// </summary>
public sealed class GatewayRegistrationClientGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Runs <paramref name="work"/> holding the registration gate (serialised).</summary>
    public async Task RunAsync(Func<CancellationToken, Task> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await work(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
