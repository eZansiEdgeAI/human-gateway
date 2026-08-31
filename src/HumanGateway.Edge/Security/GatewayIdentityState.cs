namespace HumanGateway.Edge.Security;

/// <summary>
/// The Edge's local view of its gateway registration lifecycle (AUTH-FR-01, SP-02). Mirrors the protocol's
/// <c>GatewayStatus</c> but only the states the Edge itself can observe or drive: a gateway starts
/// <see cref="Unregistered"/> (or becomes so when its stored token is missing/corrupt), is
/// <see cref="Pending"/> between the two handshake steps, and is <see cref="Registered"/> once the Relay has
/// confirmed its token. SUSPENDED/REVOKED are Relay-side states surfaced via sync rejections; the Edge does
/// not persist them locally.
/// </summary>
public enum GatewayIdentityState
{
    /// <summary>No usable registration token is stored; the Edge must run the registration handshake.</summary>
    Unregistered,

    /// <summary>A registration token has been issued but the confirm step has not completed (or the stored
    /// state is stale). A Pending identity cannot sync.</summary>
    Pending,

    /// <summary>The Relay has confirmed the token; the Edge may exchange sync batches (SP-02).</summary>
    Registered,
}
