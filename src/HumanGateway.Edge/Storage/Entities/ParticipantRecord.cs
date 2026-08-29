using HumanGateway.Protocol.Models;

namespace HumanGateway.Edge.Storage.Entities;

/// <summary>
/// Durable local participant directory (EDGE-FR-02). Resolves a typed address
/// (<c>human:</c>/<c>agent:</c>/<c>system:</c>) to its display metadata. The address is the primary key; the
/// full envelope is kept as canonical JSON, with denormalised columns for lookups and display.
/// </summary>
public sealed class ParticipantRecord
{
    /// <summary>Typed address (e.g. <c>human:teacher@school.example</c>) — the primary key.</summary>
    public string Address { get; set; } = null!;

    /// <summary>Wire-token participant kind (<c>human</c>, <c>agent</c>, <c>system</c>) for filtering.</summary>
    public string? Kind { get; set; }

    /// <summary>Human-readable display name.</summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>Optional local user identifier for human participants (AUTH-FR-02).</summary>
    public string? UserId { get; set; }

    /// <summary>Optional gateway identity for system participants (AUTH-FR-01).</summary>
    public string? GatewayId { get; set; }

    /// <summary>The full protocol participant record, stored as canonical wire JSON.</summary>
    public Participant Envelope { get; set; } = null!;

    /// <summary>Creates a storage record from a protocol participant, deriving the query columns.</summary>
    public static ParticipantRecord FromParticipant(Participant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        return new ParticipantRecord
        {
            Address = participant.Address,
            Kind = ProtocolJsonConversions.WireToken(participant.Kind),
            DisplayName = participant.DisplayName,
            UserId = participant.UserId,
            GatewayId = participant.GatewayId,
            Envelope = participant,
        };
    }
}
