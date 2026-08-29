using HumanGateway.Protocol.Models;

namespace HumanGateway.Edge.Storage.Entities;

/// <summary>
/// Durable local storage of a per-recipient <see cref="Delivery"/> lifecycle record (EDGE-FR-02, PROTO-FR-05).
/// One row per (message, recipient); the state column is denormalised for indexed status queries (the PWA
/// delivery-status indicator and the sync worker's retry scan both read it).
/// </summary>
public sealed class DeliveryRecord
{
    /// <summary>Durable delivery record ID — the primary key.</summary>
    public string Id { get; set; } = null!;

    /// <summary>The message this delivery tracks (indexed).</summary>
    public string MessageId { get; set; } = null!;

    /// <summary>The recipient's typed address (indexed).</summary>
    public string RecipientAddress { get; set; } = null!;

    /// <summary>Wire-token delivery state (<c>QUEUED</c>, <c>SYNCING</c>, ...) for indexed status queries.</summary>
    public string State { get; set; } = null!;

    /// <summary>The full protocol delivery record, stored as canonical wire JSON.</summary>
    public Delivery Envelope { get; set; } = null!;

    /// <summary>Creates a storage record from a protocol delivery, deriving the query columns.</summary>
    public static DeliveryRecord FromEnvelope(Delivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        return new DeliveryRecord
        {
            Id = delivery.Id,
            MessageId = delivery.MessageId,
            RecipientAddress = delivery.Recipient.Address,
            State = ProtocolJsonConversions.WireToken(delivery.State) ?? string.Empty,
            Envelope = delivery,
        };
    }
}
