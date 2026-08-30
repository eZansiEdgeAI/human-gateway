using HumanGateway.Protocol.Models;

namespace HumanGateway.Core.Delivery;

/// <summary>
/// Builds delivery acknowledgements returned to senders (SYNC-FR-05, syncbatch.schema.json#/$defs/deliveryAck).
/// The receiving side confirms DELIVERED / ACKNOWLEDGED, or reports FAILED, for each delivered message so the
/// sender can advance its own delivery records via <see cref="DeliveryTransitioner.ApplyAck"/>.
/// </summary>
public static class DeliveryAckBuilder
{
    /// <summary>
    /// Builds a DELIVERED acknowledgement for every message item in an applied (reordered) batch, from the
    /// perspective of the <paramref name="receiver"/> participant at <paramref name="acknowledgedAt"/>.
    /// </summary>
    public static IReadOnlyList<DeliveryAck> BuildDeliveredAcks(
        IEnumerable<SyncItem> appliedItems,
        Participant receiver,
        DateTimeOffset acknowledgedAt)
        => BuildAcks(appliedItems, receiver, acknowledgedAt, DeliveryAckState.Delivered);

    /// <summary>
    /// Builds an ACKNOWLEDGED acknowledgement for every message item in an applied batch — used when the
    /// recipient has confirmed the message was actually received/read (the DELIVERED → ACKNOWLEDGED step of
    /// product vision §10).
    /// </summary>
    public static IReadOnlyList<DeliveryAck> BuildAcknowledgedAcks(
        IEnumerable<SyncItem> appliedItems,
        Participant receiver,
        DateTimeOffset acknowledgedAt)
        => BuildAcks(appliedItems, receiver, acknowledgedAt, DeliveryAckState.Acknowledged);

    /// <summary>
    /// Builds a FAILED acknowledgement for every message item the receiver is permanently rejecting — reported
    /// back to the sender so it can mark its own delivery FAILED (SYNC-FR-05).
    /// </summary>
    public static IReadOnlyList<DeliveryAck> BuildFailedAcks(
        IEnumerable<SyncItem> appliedItems,
        Participant receiver,
        DateTimeOffset acknowledgedAt)
        => BuildAcks(appliedItems, receiver, acknowledgedAt, DeliveryAckState.Failed);

    /// <summary>Builds one acknowledgement for a single message.</summary>
    public static DeliveryAck BuildAck(
        Message message,
        Participant receiver,
        DateTimeOffset acknowledgedAt,
        DeliveryAckState state)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(receiver);

        return new DeliveryAck
        {
            MessageId = message.Id,
            Recipient = receiver,
            State = state,
            AcknowledgedAt = HumanGateway.Core.Time.ProtocolTime.Format(acknowledgedAt),
        };
    }

    private static IReadOnlyList<DeliveryAck> BuildAcks(
        IEnumerable<SyncItem> appliedItems,
        Participant receiver,
        DateTimeOffset acknowledgedAt,
        DeliveryAckState state)
    {
        ArgumentNullException.ThrowIfNull(appliedItems);
        ArgumentNullException.ThrowIfNull(receiver);

        var acks = new List<DeliveryAck>();
        foreach (var item in appliedItems)
        {
            if (item.Kind == SyncItemKind.Message && item.Message is { } message)
            {
                acks.Add(BuildAck(message, receiver, acknowledgedAt, state));
            }
        }
        return acks;
    }
}
