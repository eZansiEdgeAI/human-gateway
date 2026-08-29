using HumanGateway.Protocol.Models;

namespace HumanGateway.Core.Delivery;

/// <summary>
/// Enforces the message delivery lifecycle transition rules (product vision §10, PROTO-FR-05).
///
/// <code>
/// QUEUED ─▶ SYNCING ─▶ DELIVERED ─▶ ACKNOWLEDGED
///    │          │
///    ▼          ▼
/// WAITING_FOR_SYNC ─▶ (retry) ─▶ SYNCING ... ─▶ FAILED (after max retries, with alert)
/// </code>
///
/// <see cref="DeliveryState.WaitingForSync"/> is a <em>valid</em> state — offline deferral is expected, never an error.
/// <see cref="DeliveryState.Failed"/> is entered only after the retry budget is exhausted (see <see cref="CanFail"/>).
/// </summary>
public static class DeliveryStateMachine
{
    private static readonly IReadOnlySet<DeliveryState> Empty = new HashSet<DeliveryState>();

    private static readonly Dictionary<DeliveryState, IReadOnlySet<DeliveryState>> Transitions = new()
    {
        [DeliveryState.Queued] = new HashSet<DeliveryState>
        {
            DeliveryState.Syncing,
            DeliveryState.WaitingForSync,
        },
        [DeliveryState.Syncing] = new HashSet<DeliveryState>
        {
            DeliveryState.Syncing,        // retry attempt without leaving SYNCING
            DeliveryState.Delivered,
            DeliveryState.WaitingForSync,
            DeliveryState.Failed,
        },
        [DeliveryState.Delivered] = new HashSet<DeliveryState>
        {
            DeliveryState.Acknowledged,
        },
        [DeliveryState.WaitingForSync] = new HashSet<DeliveryState>
        {
            DeliveryState.Syncing,
            DeliveryState.Failed,
        },
        [DeliveryState.Acknowledged] = Empty,
        [DeliveryState.Failed] = Empty,
    };

    /// <summary>True when the state is terminal (no outgoing transitions).</summary>
    public static bool IsTerminal(DeliveryState state)
        => state is DeliveryState.Acknowledged or DeliveryState.Failed;

    /// <summary>The set of structurally legal target states from <paramref name="from"/>.</summary>
    public static IReadOnlySet<DeliveryState> AllowedTransitions(DeliveryState from)
        => Transitions.TryGetValue(from, out var set) ? set : Empty;

    /// <summary>True if <paramref name="to"/> is structurally reachable from <paramref name="from"/>.</summary>
    /// <remarks>Does not apply the <see cref="DeliveryState.Failed"/> retry-budget guard — use <see cref="CanFail"/> for that.</remarks>
    public static bool CanTransition(DeliveryState from, DeliveryState to)
        => AllowedTransitions(from).Contains(to);

    /// <summary>
    /// True when a transition to <see cref="DeliveryState.Failed"/> is permitted: the attempt budget must be exhausted
    /// (<paramref name="attempts"/> ≥ <paramref name="maxAttempts"/>) and the delivery must be in a
    /// retryable state. This is what prevents <see cref="DeliveryState.WaitingForSync"/> from being marked FAILED and
    /// alerted on merely for being offline.
    /// </summary>
    public static bool CanFail(DeliveryState from, long attempts, long maxAttempts)
        => attempts >= maxAttempts && (from is DeliveryState.Syncing or DeliveryState.WaitingForSync);
}
