namespace HumanGateway.Edge.Security;

/// <summary>
/// Port for the Edge's secret store (SP-07): durable persistence of the gateway identity including the
/// plaintext registration token. The implementation decides the backing (v1: an owner-only file under the
/// Edge data directory); anything the Edge must survive a restart with — the gateway ID and its registration
/// token — lives behind this port so a later task can swap in an OS secret store / keychain without touching
/// the identity manager.
/// </summary>
public interface IGatewaySecretStore
{
    /// <summary>
    /// Loads the persisted gateway identity (including the plaintext registration token), or null when no
    /// secret has been stored yet (first boot / never registered). A corrupt or tampered store must throw,
    /// never silently drop the identity — silently re-registering would change the gateway's identity.
    /// </summary>
    Task<GatewayIdentity?> LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists the gateway identity atomically (temp-file + rename so a crash mid-write cannot leave a
    /// half-written secret). The token is written with owner-only file permissions (SP-07).
    /// </summary>
    Task SaveAsync(GatewayIdentity identity, CancellationToken ct = default);
}
