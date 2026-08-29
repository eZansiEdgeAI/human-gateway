using System.Text.Json;
using System.Text.Json.Serialization;

namespace HumanGateway.Protocol.Models;

/// <summary>
/// The protocol error model (error.schema.json, protocol §7 #3) — a stable, machine-readable error
/// <see cref="Code"/> plus a human-readable <see cref="Message"/>, optional structured <see cref="Details"/>,
/// and a retryability hint. Referenced by delivery FAILED records and sync/API error responses. Error
/// payloads must never carry secrets, registration tokens, session tokens, or password material (SP-07).
/// </summary>
public sealed record ProtocolError
{
    /// <summary>
    /// Machine-readable, stable error code (UPPER_SNAKE, ≤ 64 chars). Reserved codes are enumerated in
    /// <see cref="ErrorCodes"/>; extensions follow the same token shape.
    /// </summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = null!;

    /// <summary>Human-readable error description; safe to display (SP-07).</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = null!;

    /// <summary>Optional machine-readable detail fields. Must not contain secrets.</summary>
    [JsonPropertyName("details")]
    public JsonElement? Details { get; init; }

    /// <summary>Retry hint: true for transient conditions, false for permanent rejections.</summary>
    [JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }
}

/// <summary>
/// The reserved protocol error-code catalog (error.schema.json#/$defs/errorCode). Stable across releases:
/// codes are added, never changed or removed. Consumers may extend the catalog using the same UPPER_SNAKE
/// token shape.
/// </summary>
public static class ErrorCodes
{
    public const string BadRequest = "BAD_REQUEST";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string InternalError = "INTERNAL_ERROR";
    public const string Timeout = "TIMEOUT";
    public const string RateLimited = "RATE_LIMITED";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string AuthRejected = "AUTH_REJECTED";
    public const string SignatureInvalid = "SIGNATURE_INVALID";
    public const string SessionTokenInvalid = "SESSION_TOKEN_INVALID";
    public const string SessionTokenExpired = "SESSION_TOKEN_EXPIRED";
    public const string Forbidden = "FORBIDDEN";
    public const string ConversationAccessDenied = "CONVERSATION_ACCESS_DENIED";
    public const string TaskAccessDenied = "TASK_ACCESS_DENIED";
    public const string ArtifactAccessDenied = "ARTIFACT_ACCESS_DENIED";
    public const string GatewayUnregistered = "GATEWAY_UNREGISTERED";
    public const string GatewaySuspended = "GATEWAY_SUSPENDED";
    public const string GatewayRevoked = "GATEWAY_REVOKED";
    public const string RegistrationTokenInvalid = "REGISTRATION_TOKEN_INVALID";
    public const string RegistrationTokenExpired = "REGISTRATION_TOKEN_EXPIRED";
    public const string ArtifactNotFound = "ARTIFACT_NOT_FOUND";
    public const string HashMismatch = "HASH_MISMATCH";
    public const string SizeExceeded = "SIZE_EXCEEDED";
    public const string QuotaExceeded = "QUOTA_EXCEEDED";
    public const string MaxAttemptsExceeded = "MAX_ATTEMPTS_EXCEEDED";
    public const string MessageExpired = "MESSAGE_EXPIRED";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string SequenceGap = "SEQUENCE_GAP";
    public const string CursorInvalid = "CURSOR_INVALID";
}
