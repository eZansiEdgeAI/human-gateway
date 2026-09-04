using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace HumanGateway.Protocol.Models;

/// <summary>Local Edge user account status (user.schema.json, AUTH-FR-02).</summary>
public enum UserStatus
{
    [EnumMember(Value = "ACTIVE")]
    Active,
    [EnumMember(Value = "DISABLED")]
    Disabled,
}

public enum UserRole
{
    [EnumMember(Value = "USER")]
    User,
    [EnumMember(Value = "ADMIN")]
    Admin,
}

/// <summary>
/// A local Edge user account (user.schema.json, AUTH-FR-02). <see cref="PasswordVerifier"/> is the
/// credential verifier (PHC string) and is a local-store-only field — it MUST NEVER be transmitted in any
/// protocol payload and never logged (SP-07). Referenced from message flows via participant.userId on
/// human: participants.
/// </summary>
public sealed record User
{
    /// <summary>Durable local user ID. Referenced from participant.userId for human participants.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = null!;

    /// <summary>Login username (lowercase; matching is case-insensitive).</summary>
    [JsonPropertyName("username")]
    public string Username { get; init; } = null!;

    /// <summary>Human-readable name shown in the UI.</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = null!;

    /// <summary>Password verifier in PHC string format (e.g. argon2id, bcrypt). Edge-local store field ONLY (SP-07).</summary>
    [JsonPropertyName("passwordVerifier")]
    public string PasswordVerifier { get; init; } = null!;

    /// <summary>ACTIVE users may log in; DISABLED users are rejected at authentication time.</summary>
    [JsonPropertyName("status")]
    public UserStatus? Status { get; init; }

    [JsonPropertyName("role")]
    public UserRole Role { get; init; } = UserRole.User;

    [JsonPropertyName("lastLoginAt")]
    public string? LastLoginAt { get; init; }

    /// <summary>Required when status is DISABLED.</summary>
    [JsonPropertyName("disabledAt")]
    public string? DisabledAt { get; init; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = null!;

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; init; }
}
