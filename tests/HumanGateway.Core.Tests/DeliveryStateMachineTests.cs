using HumanGateway.Core.Delivery;
using HumanGateway.Protocol.Models;
using Xunit;

namespace HumanGateway.Core.Tests;

/// <summary>
/// Pins the delivery state machine to the transition table in product vision §10. In particular:
/// <c>WAITING_FOR_SYNC</c> is a valid (non-terminal) state and must never transition to FAILED merely for
/// being offline — FAILED is only reachable once the retry budget is exhausted.
/// </summary>
public class DeliveryStateMachineTests
{
    [Fact]
    public void Queued_can_enter_syncing_or_waiting_for_sync()
    {
        Assert.True(DeliveryStateMachine.CanTransition(DeliveryState.Queued, DeliveryState.Syncing));
        Assert.True(DeliveryStateMachine.CanTransition(DeliveryState.Queued, DeliveryState.WaitingForSync));
        Assert.False(DeliveryStateMachine.CanTransition(DeliveryState.Queued, DeliveryState.Delivered));
        Assert.False(DeliveryStateMachine.CanTransition(DeliveryState.Queued, DeliveryState.Failed));
    }

    [Fact]
    public void WaitingForSync_is_valid_and_not_terminal()
    {
        Assert.False(DeliveryStateMachine.IsTerminal(DeliveryState.WaitingForSync));
        Assert.True(DeliveryStateMachine.CanTransition(DeliveryState.WaitingForSync, DeliveryState.Syncing));
    }

    [Fact]
    public void WaitingForSync_never_transitions_to_failed_directly()
    {
        // Structurally the transition exists (for exhausted retry budget), but CanFail must gate it.
        Assert.False(DeliveryStateMachine.CanFail(DeliveryState.WaitingForSync, attempts: 2, maxAttempts: 8));
        Assert.True(DeliveryStateMachine.CanFail(DeliveryState.WaitingForSync, attempts: 8, maxAttempts: 8));
    }

    [Fact]
    public void Delivered_can_only_be_acknowledged()
    {
        Assert.True(DeliveryStateMachine.CanTransition(DeliveryState.Delivered, DeliveryState.Acknowledged));
        Assert.False(DeliveryStateMachine.CanTransition(DeliveryState.Delivered, DeliveryState.Failed));
        Assert.False(DeliveryStateMachine.CanTransition(DeliveryState.Delivered, DeliveryState.Syncing));
    }

    [Fact]
    public void Terminal_states_have_no_outgoing_transitions()
    {
        Assert.True(DeliveryStateMachine.IsTerminal(DeliveryState.Acknowledged));
        Assert.True(DeliveryStateMachine.IsTerminal(DeliveryState.Failed));
        Assert.Empty(DeliveryStateMachine.AllowedTransitions(DeliveryState.Acknowledged));
        Assert.Empty(DeliveryStateMachine.AllowedTransitions(DeliveryState.Failed));
    }

    [Fact]
    public void Failed_requires_exhausted_retry_budget_and_retryable_state()
    {
        Assert.True(DeliveryStateMachine.CanFail(DeliveryState.Syncing, attempts: 8, maxAttempts: 8));
        Assert.False(DeliveryStateMachine.CanFail(DeliveryState.Syncing, attempts: 7, maxAttempts: 8));
        Assert.False(DeliveryStateMachine.CanFail(DeliveryState.Queued, attempts: 8, maxAttempts: 8));
        Assert.False(DeliveryStateMachine.CanFail(DeliveryState.Delivered, attempts: 8, maxAttempts: 8));
    }
}
