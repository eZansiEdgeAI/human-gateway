using HumanGateway.Core.Delivery;
using HumanGateway.Core.Ids;
using HumanGateway.Protocol.Models;
using Xunit;

// using HumanGateway.Core.Delivery imports the *namespace* "Delivery", which would shadow the protocol
// "Delivery" *type*; alias the type so both are available unambiguously.
using DeliveryEnvelope = HumanGateway.Protocol.Models.Delivery;

namespace HumanGateway.Core.Tests;

/// <summary>
/// Pins <see cref="DeliveryTransitioner"/> to product vision §10 (QUEUED → SYNCING → DELIVERED →
/// ACKNOWLEDGED, with WAITING_FOR_SYNC deferral and FAILED gated by the retry budget) and to SYNC-FR-05
/// (acknowledgements returned to senders advance the sender's delivery record idempotently). In particular:
/// offline deferral is never FAILED, FAILED requires both error details and an exhausted retry budget, and a
/// replayed ack never regresses a delivery (NF-05).
/// </summary>
public class DeliveryTransitionerTests
{
    private static readonly DateTimeOffset T = TestData.FixedNow;
    private static readonly string TString = "2026-08-29T00:00:00.000Z";

    private static DeliveryEnvelope In(DeliveryState state, long attempts = 0, long maxAttempts = 5) => new()
    {
        Id = IdGenerator.NewId(),
        MessageId = "message:" + IdGenerator.NewId(),
        Recipient = TestData.Receiver,
        State = state,
        Attempts = attempts,
        MaxAttempts = maxAttempts,
        QueuedAt = TString,
        CreatedAt = TString,
        UpdatedAt = TString,
    };

    private static DeliveryAck Ack(DeliveryAckState state, string? messageId = null) => new()
    {
        MessageId = messageId ?? "message:target",
        Recipient = TestData.Receiver,
        State = state,
        AcknowledgedAt = TString,
    };

    // -----------------------------------------------------------------------------------------------
    // Transitions (product vision §10)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void Queued_to_syncing_increments_attempts_and_stamps_syncing_at()
    {
        var result = DeliveryTransitioner.Transition(In(DeliveryState.Queued), DeliveryState.Syncing, T);

        Assert.True(result.IsValid);
        Assert.Equal(DeliveryState.Syncing, result.Delivery!.State);
        Assert.Equal(1, result.Delivery.Attempts);
        Assert.Equal(TString, result.Delivery.SyncingAt);
    }

    [Fact]
    public void Syncing_to_delivered_stamps_delivered_at_without_incrementing_attempts()
    {
        var result = DeliveryTransitioner.Transition(In(DeliveryState.Syncing, attempts: 2), DeliveryState.Delivered, T);

        Assert.True(result.IsValid);
        Assert.Equal(DeliveryState.Delivered, result.Delivery!.State);
        Assert.Equal(2, result.Delivery.Attempts);
        Assert.Equal(TString, result.Delivery.DeliveredAt);
    }

    [Fact]
    public void Delivered_to_acknowledged_stamps_acknowledged_at()
    {
        var result = DeliveryTransitioner.Transition(In(DeliveryState.Delivered), DeliveryState.Acknowledged, T);

        Assert.True(result.IsValid);
        Assert.Equal(DeliveryState.Acknowledged, result.Delivery!.State);
        Assert.Equal(TString, result.Delivery.AcknowledgedAt);
    }

    [Fact]
    public void Syncing_to_waiting_for_sync_stamps_deferral_timestamp()
    {
        var result = DeliveryTransitioner.Transition(In(DeliveryState.Syncing), DeliveryState.WaitingForSync, T);

        Assert.True(result.IsValid);
        Assert.Equal(DeliveryState.WaitingForSync, result.Delivery!.State);
        Assert.Equal(TString, result.Delivery.WaitingForSyncAt);
        Assert.False(DeliveryStateMachine.IsTerminal(result.Delivery.State!.Value));
    }

    [Fact]
    public void WaitingForSync_retry_back_to_syncing_increments_attempts()
    {
        var result = DeliveryTransitioner.Transition(
            In(DeliveryState.WaitingForSync, attempts: 3),
            DeliveryState.Syncing,
            T);

        Assert.True(result.IsValid);
        Assert.Equal(DeliveryState.Syncing, result.Delivery!.State);
        Assert.Equal(4, result.Delivery.Attempts);
    }

    [Fact]
    public void Reentering_a_state_is_an_idempotent_noop()
    {
        var current = In(DeliveryState.Delivered, attempts: 1);

        var result = DeliveryTransitioner.Transition(current, DeliveryState.Delivered, T);

        Assert.True(result.IsValid);
        Assert.Same(current, result.Delivery);
    }

    [Fact]
    public void Illegal_transition_is_rejected()
    {
        var result = DeliveryTransitioner.Transition(In(DeliveryState.Queued), DeliveryState.Delivered, T);

        Assert.False(result.IsValid);
        Assert.Equal(DeliveryTransitioner.IllegalTransition, result.Violation);
    }

    // -----------------------------------------------------------------------------------------------
    // FAILED gating (product vision §10)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void Failed_requires_error_details()
    {
        var result = DeliveryTransitioner.Transition(
            In(DeliveryState.Syncing, attempts: 5, maxAttempts: 5),
            DeliveryState.Failed,
            T,
            error: null);

        Assert.False(result.IsValid);
        Assert.Equal(DeliveryTransitioner.ErrorRequired, result.Violation);
    }

    [Fact]
    public void Failed_requires_exhausted_retry_budget()
    {
        var result = DeliveryTransitioner.Transition(
            In(DeliveryState.Syncing, attempts: 4, maxAttempts: 5),
            DeliveryState.Failed,
            T,
            error: new ProtocolError { Code = ErrorCodes.MaxAttemptsExceeded, Message = "budget" });

        Assert.False(result.IsValid);
        Assert.Equal(DeliveryTransitioner.RetryBudgetNotExhausted, result.Violation);
    }

    [Fact]
    public void Failed_succeeds_when_budget_exhausted_and_error_present()
    {
        var result = DeliveryTransitioner.Transition(
            In(DeliveryState.Syncing, attempts: 5, maxAttempts: 5),
            DeliveryState.Failed,
            T,
            error: new ProtocolError { Code = ErrorCodes.MaxAttemptsExceeded, Message = "budget" });

        Assert.True(result.IsValid);
        Assert.Equal(DeliveryState.Failed, result.Delivery!.State);
        Assert.Equal(TString, result.Delivery.FailedAt);
        Assert.Equal(ErrorCodes.MaxAttemptsExceeded, result.Delivery.Error!.Code);
    }

    // -----------------------------------------------------------------------------------------------
    // Ack application (SYNC-FR-05)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void ApplyAck_delivered_advances_syncing_to_delivered()
    {
        var result = DeliveryTransitioner.ApplyAck(In(DeliveryState.Syncing), Ack(DeliveryAckState.Delivered), T);

        Assert.True(result.IsValid);
        Assert.Equal(DeliveryState.Delivered, result.Delivery!.State);
    }

    [Fact]
    public void ApplyAck_acknowledged_advances_delivered_to_acknowledged()
    {
        var result = DeliveryTransitioner.ApplyAck(In(DeliveryState.Delivered), Ack(DeliveryAckState.Acknowledged), T);

        Assert.True(result.IsValid);
        Assert.Equal(DeliveryState.Acknowledged, result.Delivery!.State);
    }

    [Fact]
    public void ApplyAck_replayed_delivered_ack_is_a_noop()
    {
        var current = In(DeliveryState.Delivered);

        var result = DeliveryTransitioner.ApplyAck(current, Ack(DeliveryAckState.Delivered), T);

        Assert.True(result.IsValid);
        Assert.Same(current, result.Delivery);
    }

    [Fact]
    public void ApplyAck_stale_ack_never_regresses_a_terminal_delivery()
    {
        var acknowledged = In(DeliveryState.Acknowledged);

        var result = DeliveryTransitioner.ApplyAck(acknowledged, Ack(DeliveryAckState.Delivered), T);

        Assert.True(result.IsValid);
        Assert.Same(acknowledged, result.Delivery);
    }

    [Fact]
    public void ApplyAck_failed_marks_permanent_rejection_regardless_of_budget()
    {
        // attempts (1) << maxAttempts (5): the sender's own retry budget is nowhere near exhausted, yet a
        // permanent recipient rejection must still fail the delivery.
        var result = DeliveryTransitioner.ApplyAck(
            In(DeliveryState.Syncing, attempts: 1, maxAttempts: 5),
            Ack(DeliveryAckState.Failed, "message:target"),
            T);

        Assert.True(result.IsValid);
        Assert.Equal(DeliveryState.Failed, result.Delivery!.State);
        Assert.Equal(ErrorCodes.DeliveryRejected, result.Delivery.Error!.Code);
        Assert.Equal(TString, result.Delivery.FailedAt);
    }

    [Fact]
    public void ApplyAck_failed_does_not_regress_a_delivered_delivery()
    {
        var delivered = In(DeliveryState.Delivered);

        var result = DeliveryTransitioner.ApplyAck(delivered, Ack(DeliveryAckState.Failed), T);

        Assert.True(result.IsValid);
        Assert.Same(delivered, result.Delivery);
    }

    // -----------------------------------------------------------------------------------------------
    // Ack builders (SYNC-FR-05)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void AckBuilders_emit_the_requested_state_for_each_message_item()
    {
        var items = new[]
        {
            TestData.MessageItem(TestData.NewMessage("message:one"), 1),
            TestData.MessageItem(TestData.NewMessage("message:two"), 2),
        };

        var delivered = DeliveryAckBuilder.BuildDeliveredAcks(items, TestData.Receiver, T);
        var acknowledged = DeliveryAckBuilder.BuildAcknowledgedAcks(items, TestData.Receiver, T);
        var failed = DeliveryAckBuilder.BuildFailedAcks(items, TestData.Receiver, T);

        Assert.All(delivered, a => Assert.Equal(DeliveryAckState.Delivered, a.State));
        Assert.All(acknowledged, a => Assert.Equal(DeliveryAckState.Acknowledged, a.State));
        Assert.All(failed, a => Assert.Equal(DeliveryAckState.Failed, a.State));
        Assert.Equal(new[] { "message:one", "message:two" }, delivered.Select(a => a.MessageId).ToArray());
        Assert.All(delivered, a => Assert.Equal(TestData.Receiver.Address, a.Recipient.Address));
    }
}
