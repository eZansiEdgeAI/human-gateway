using HumanGateway.Core.Time;
using HumanGateway.Protocol.Models;

// The Delivery *type* (Protocol.Models.Delivery) shares its simple name with this namespace
// (HumanGateway.Core.Delivery). A using alias named "Delivery" cannot shadow the enclosing namespace's
// member, so alias the type under a distinct name to keep the references unambiguous.
using DeliveryEnvelope = HumanGateway.Protocol.Models.Delivery;

namespace HumanGateway.Core.Delivery;

/// <summary>
/// Performs delivery-state transitions on a <see cref="DeliveryEnvelope"/> (product vision §10, SYNC-FR-05).
/// This is the deterministic, pure counterpart to <see cref="DeliveryStateMachine"/>: the state machine answers
/// "is this transition legal?", while the transitioner performs it — stamping the state-entry timestamp,
/// advancing the attempt counter where appropriate, and attaching failure details — and returns the <em>next</em>
/// envelope. It never mutates its input.
///
/// Two entry points mirror the two sides of the store-and-forward contract:
/// <list type="bullet">
/// <item><see cref="Transition"/> drives the sender's own lifecycle: <c>QUEUED → SYNCING → DELIVERED →
/// ACKNOWLEDGED</c>, with <c>WAITING_FOR_SYNC</c> for offline deferral and <c>FAILED</c> gated by the retry
/// budget (<see cref="DeliveryStateMachine.CanFail"/>).</item>
/// <item><see cref="ApplyAck"/> consumes a delivery acknowledgement returned by the recipient (SYNC-FR-05):
/// <c>DELIVERED</c>/<c>ACKNOWLEDGED</c> advance the sender's record, and <c>FAILED</c> marks a permanent
/// recipient rejection (independent of the sender's retry budget).</item>
/// </list>
///
/// Both are idempotent (at-least-once → exactly-once effect, NF-05): a replayed transition or a replayed ack
/// that has already been satisfied has no further effect.
/// </summary>
public static class DeliveryTransitioner
{
    /// <summary>The transition is not in the allowed set for the current state.</summary>
    public const string IllegalTransition = "ILLEGAL_TRANSITION";

    /// <summary>A transition to FAILED requires the retry budget to be exhausted.</summary>
    public const string RetryBudgetNotExhausted = "RETRY_BUDGET_NOT_EXHAUSTED";

    /// <summary>A transition to FAILED requires error details (delivery.schema.json allOf).</summary>
    public const string ErrorRequired = "ERROR_REQUIRED";

    /// <summary>
    /// Transitions <paramref name="current"/> to <paramref name="target"/> at <paramref name="at"/>, returning
    /// the next envelope. Entering <c>SYNCING</c> (from <c>QUEUED</c> or <c>WAITING_FOR_SYNC</c>) or retrying
    /// in <c>SYNCING</c> advances the attempt counter; every other transition preserves it. A transition to
    /// <c>FAILED</c> requires <paramref name="error"/> and an exhausted retry budget. Re-entering a non-SYNCING
    /// state is an idempotent no-op that returns <paramref name="current"/> unchanged.
    /// </summary>
    public static DeliveryTransitionResult Transition(
        DeliveryEnvelope current,
        DeliveryState target,
        DateTimeOffset at,
        ProtocolError? error = null)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current.State is not { } from)
        {
            return DeliveryTransitionResult.Invalid(IllegalTransition);
        }

        // Idempotent no-op: already in the target state. SYNCING is the exception — a SYNCING → SYNCING
        // transition is a retry attempt, which advances the attempt counter.
        if (from == target && target != DeliveryState.Syncing)
        {
            return DeliveryTransitionResult.Success(current);
        }

        if (target == DeliveryState.Failed)
        {
            if (error is null)
            {
                return DeliveryTransitionResult.Invalid(ErrorRequired);
            }
            if (!DeliveryStateMachine.CanTransition(from, DeliveryState.Failed))
            {
                return DeliveryTransitionResult.Invalid(IllegalTransition);
            }
            if (!DeliveryStateMachine.CanFail(from, current.Attempts, current.MaxAttempts))
            {
                return DeliveryTransitionResult.Invalid(RetryBudgetNotExhausted);
            }
        }
        else if (!DeliveryStateMachine.CanTransition(from, target))
        {
            return DeliveryTransitionResult.Invalid(IllegalTransition);
        }

        var now = ProtocolTime.Format(at);
        var attempts = target == DeliveryState.Syncing ? current.Attempts + 1 : current.Attempts;

        var next = current with
        {
            State = target,
            Attempts = attempts,
            UpdatedAt = now,
            SyncingAt = target == DeliveryState.Syncing ? now : current.SyncingAt,
            WaitingForSyncAt = target == DeliveryState.WaitingForSync ? now : current.WaitingForSyncAt,
            DeliveredAt = target == DeliveryState.Delivered ? now : current.DeliveredAt,
            AcknowledgedAt = target == DeliveryState.Acknowledged ? now : current.AcknowledgedAt,
            FailedAt = target == DeliveryState.Failed ? now : current.FailedAt,
            Error = target == DeliveryState.Failed ? error : current.Error,
        };

        return DeliveryTransitionResult.Success(next);
    }

    /// <summary>
    /// Applies a delivery acknowledgement received from the recipient (SYNC-FR-05) to the sender's delivery
    /// record: <c>DELIVERED</c> advances <c>SYNCING → DELIVERED</c>, <c>ACKNOWLEDGED</c> advances
    /// <c>DELIVERED → ACKNOWLEDGED</c>, and <c>FAILED</c> marks a permanent recipient rejection. Idempotent —
    /// a replayed ack that has already been satisfied (or superseded by a later terminal state) is a no-op, so
    /// at-least-once ack delivery converges to exactly-once effect (NF-05).
    /// </summary>
    public static DeliveryTransitionResult ApplyAck(DeliveryEnvelope current, DeliveryAck ack, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(ack);

        var target = ack.State switch
        {
            DeliveryAckState.Delivered => DeliveryState.Delivered,
            DeliveryAckState.Acknowledged => DeliveryState.Acknowledged,
            DeliveryAckState.Failed => DeliveryState.Failed,
            _ => (DeliveryState?)null,
        };

        if (target is not { } t)
        {
            return DeliveryTransitionResult.Invalid(IllegalTransition);
        }

        // Idempotency: a replayed (at-least-once) ack must never regress a delivery. If the delivery has
        // already reached the ack's target state — or a later state that subsumes it — the ack has no effect.
        if (AlreadySatisfied(current.State, t))
        {
            return DeliveryTransitionResult.Success(current);
        }

        // FAILED acks are permanent recipient rejections, not retry-budget exhaustion, so they bypass CanFail.
        if (t == DeliveryState.Failed)
        {
            return ForceReject(current, ack, at);
        }

        return Transition(current, t, at);
    }

    /// <summary>Marks a permanent recipient rejection as FAILED, from an in-flight (non-terminal, non-delivered) state.</summary>
    private static DeliveryTransitionResult ForceReject(DeliveryEnvelope current, DeliveryAck ack, DateTimeOffset at)
    {
        if (current.State is not (DeliveryState.Queued or DeliveryState.Syncing or DeliveryState.WaitingForSync))
        {
            // DELIVERED / ACKNOWLEDGED / FAILED already reached: never regress.
            return DeliveryTransitionResult.Success(current);
        }

        var now = ProtocolTime.Format(at);
        var next = current with
        {
            State = DeliveryState.Failed,
            UpdatedAt = now,
            FailedAt = now,
            Error = new ProtocolError
            {
                Code = ErrorCodes.DeliveryRejected,
                Message = $"Recipient rejected delivery of message {ack.MessageId}.",
                Retryable = false,
            },
        };

        return DeliveryTransitionResult.Success(next);
    }

    /// <summary>
    /// True when the delivery is already at (or past) the ack's target, so the ack can be discarded without
    /// effect. Terminal states subsume earlier acknowledgements (an <c>ACKNOWLEDGED</c> delivery is trivially
    /// <c>DELIVERED</c>); a delivery is never regressed by a stale ack.
    /// </summary>
    private static bool AlreadySatisfied(DeliveryState? current, DeliveryState target) => target switch
    {
        DeliveryState.Delivered => current is DeliveryState.Delivered or DeliveryState.Acknowledged or DeliveryState.Failed,
        DeliveryState.Acknowledged => current is DeliveryState.Acknowledged or DeliveryState.Failed,
        DeliveryState.Failed => current is DeliveryState.Delivered or DeliveryState.Acknowledged or DeliveryState.Failed,
        _ => current == target,
    };
}

/// <summary>The result of a delivery-state transition (see <see cref="DeliveryTransitioner"/>).</summary>
public sealed record DeliveryTransitionResult
{
    /// <summary>The next delivery envelope, or null when the transition was rejected.</summary>
    public DeliveryEnvelope? Delivery { get; init; }

    /// <summary>The violation code when the transition was rejected (see <see cref="DeliveryTransitioner"/>).</summary>
    public string? Violation { get; init; }

    /// <summary>True when the transition succeeded (including idempotent no-ops).</summary>
    public bool IsValid => Delivery is not null;

    /// <summary>Builds a successful result carrying the next envelope.</summary>
    public static DeliveryTransitionResult Success(DeliveryEnvelope delivery) => new() { Delivery = delivery };

    /// <summary>Builds a rejected result carrying the violation code.</summary>
    public static DeliveryTransitionResult Invalid(string violation) => new() { Violation = violation };
}
