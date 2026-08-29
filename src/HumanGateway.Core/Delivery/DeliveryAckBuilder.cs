using HumanGateway.Protocol.Models;

namespace HumanGateway.Core.Delivery;

/// <summary>
/// Builds delivery acknowledgements returned to senders (SYNC-FR-05, syncbatch.schema.json#/$defs/deliveryAck).
/// The receiving side confirms DELIVERED / ACKNOWLEDGED, or reports FAILED, for each delivered message so the
/// sender can advance its own delivery records.
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
    {
        ArgumentNullException.ThrowIfNull(appliedItems);
        ArgumentNullException.ThrowIfNull(receiver);

        var timestamp = HumanGateway.Core.Time.ProtocolTime.Format(acknowledgedAt);
        var acks = new List<DeliveryAck>();
        foreach (var item in appliedItems)
        {
            if (item.Kind == SyncItemKind.Message && item.Message is { } message)
            {
                acks.Add(new DeliveryAck
                {
                    MessageId = message.Id,
                    Recipient = receiver,
                    State = DeliveryAckState.Delivered,
                    AcknowledgedAt = timestamp,
                });
            }
        }
        return acks;
    }
}
