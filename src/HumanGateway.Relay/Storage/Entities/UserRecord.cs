using HumanGateway.Protocol.Models;

namespace HumanGateway.Relay.Storage.Entities;

/// <summary>
/// Durable remote user account at the Relay (user.schema.json, AUTH-FR-02, external-web-access). Remote users
/// authenticate at the Relay with a username + password (the remote login gate) before the PWA can reach
/// external web access. The full protocol <see cref="User"/> envelope — including the PHC
/// <c>passwordVerifier</c> — is stored as canonical wire JSON (local store ONLY; the verifier is never
/// transmitted in any protocol payload, SP-07). Denormalised columns (username, status) exist for indexed
/// login lookups.
/// </summary>
public sealed class UserRecord
{
    /// <summary>Durable remote user id (user.schema.json#/id). Referenced from participant.userId.</summary>
    public string Id { get; set; } = null!;

    /// <summary>Login username, normalised to lowercase (unique).</summary>
    public string Username { get; set; } = null!;

    /// <summary>Wire-token status (<c>ACTIVE</c> | <c>DISABLED</c>) for indexed filtering.</summary>
    public string? Status { get; set; }

    /// <summary>RFC 3339 UTC instant of the last successful login.</summary>
    public string? LastLoginAt { get; set; }

    /// <summary>The full protocol user record (canonical wire JSON, verifier included — local store only).</summary>
    public User Envelope { get; set; } = null!;

    /// <summary>Creates a storage record from a protocol user, deriving the denormalised query columns.</summary>
    public static UserRecord FromEnvelope(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserRecord
        {
            Id = user.Id,
            Username = user.Username,
            Status = RelayJsonConversions.WireToken(user.Status),
            LastLoginAt = user.LastLoginAt,
            Envelope = user,
        };
    }

    /// <summary>Refreshes the denormalised columns from the envelope after an in-place update.</summary>
    public void RefreshDenormalised()
    {
        Username = Envelope.Username;
        Status = RelayJsonConversions.WireToken(Envelope.Status);
        LastLoginAt = Envelope.LastLoginAt;
    }
}
