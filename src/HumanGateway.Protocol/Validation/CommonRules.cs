using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HumanGateway.Protocol.Validation;

/// <summary>
/// Shared rule implementations mirroring the <c>$defs</c> of common.schema.json (IDs, RFC 3339 timestamps,
/// content hashes, participant addresses, cursors, idempotency keys, correlation tokens) plus the string/
/// numeric bounds used across the entity schemas. The schemas under <c>schemas/</c> remain the single
/// source of truth (NF-06); these rules never diverge from them.
/// </summary>
public static partial class CommonRules
{
    // ---- Wire-format patterns, copied verbatim from the v1 schemas (common.schema.json and friends) ----

    /// <summary>common.schema.json#/$defs/id — durable ID, 8..128 chars.</summary>
    public const string IdPattern = "^[A-Za-z0-9][A-Za-z0-9._:-]{7,127}$";

    /// <summary>common.schema.json#/$defs/timestamp — RFC 3339 UTC with trailing 'Z'.</summary>
    public const string TimestampPattern = "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\\.[0-9]{1,9})?Z$";

    /// <summary>common.schema.json#/$defs/contentHash — sha256:&lt;64 lowercase hex&gt;.</summary>
    public const string ContentHashPattern = "^sha256:[0-9a-f]{64}$";

    /// <summary>common.schema.json#/$defs/participantAddress — typed address (human|agent|system):suffix.</summary>
    public const string ParticipantAddressPattern = "^(human|agent|system):[A-Za-z0-9._@+-]{1,191}$";

    /// <summary>syncbatch.schema.json#/$defs/cursor — opaque URL-safe token, ≤ 1024 chars.</summary>
    public const string CursorPattern = "^[A-Za-z0-9._:/-]*$";

    /// <summary>syncbatch.schema.json#/idempotencyKey — 1..128 chars.</summary>
    public const string IdempotencyKeyPattern = "^[A-Za-z0-9._:-]+$";

    /// <summary>artifact.schema.json#/mimeType — type/subtype token pair.</summary>
    public const string MimeTypePattern = "^[A-Za-z0-9!#$&^_.+-]+/[A-Za-z0-9!#$&^_.+-]+$";

    /// <summary>error.schema.json#/code — UPPER_SNAKE token, ≤ 64 chars (extensions allowed).</summary>
    public const string ErrorCodePattern = "^[A-Z][A-Z0-9_]*$";

    /// <summary>user.schema.json#/username — lowercase login username, 3..64 chars.</summary>
    public const string UsernamePattern = "^[a-z0-9][a-z0-9._-]{2,63}$";

    /// <summary>user.schema.json#/passwordVerifier — PHC string, 20..512 chars.</summary>
    public const string PasswordVerifierPattern = "^\\$[A-Za-z0-9+./=,$-]{19,511}$";

    /// <summary>gateway.schema.json#/registrationTokenFingerprint — sha256:&lt;64 lowercase hex&gt;.</summary>
    public const string Sha256FingerprintPattern = "^sha256:[0-9a-f]{64}$";

    /// <summary>gateway.schema.json#/$defs/registrationToken — <c>hgrt_</c> + base64url body (48..256 chars).</summary>
    public const string RegistrationTokenPattern = "^hgrt_[A-Za-z0-9_-]{43,251}$";

    /// <summary>artifact.schema.json#/sizeBytes maximum — protocol ceiling of 512 MiB.</summary>
    public const long MaxArtifactSizeBytes = 536_870_912;

    /// <summary>message.schema.json#/payload.body maximum length.</summary>
    public const int MaxMessageBodyLength = 1_000_000;

    /// <summary>humantask.schema.json#/prompt maximum length.</summary>
    public const int MaxPromptLength = 100_000;

    /// <summary>syncbatch.schema.json#/items maximum entries per batch.</summary>
    public const int MaxSyncItemsPerBatch = 1_000;

    // ---- Compiled pattern matchers ----

    [GeneratedRegex(IdPattern)]
    internal static partial Regex IdRegex();

    [GeneratedRegex(TimestampPattern)]
    internal static partial Regex TimestampRegex();

    [GeneratedRegex(ContentHashPattern)]
    internal static partial Regex ContentHashRegex();

    [GeneratedRegex(ParticipantAddressPattern)]
    internal static partial Regex ParticipantAddressRegex();

    [GeneratedRegex(CursorPattern)]
    internal static partial Regex CursorRegex();

    [GeneratedRegex(IdempotencyKeyPattern)]
    internal static partial Regex IdempotencyKeyRegex();

    [GeneratedRegex(MimeTypePattern)]
    internal static partial Regex MimeTypeRegex();

    [GeneratedRegex(ErrorCodePattern)]
    internal static partial Regex ErrorCodeRegex();

    [GeneratedRegex(UsernamePattern)]
    internal static partial Regex UsernameRegex();

    [GeneratedRegex(PasswordVerifierPattern)]
    internal static partial Regex PasswordVerifierRegex();

    [GeneratedRegex(Sha256FingerprintPattern)]
    internal static partial Regex Sha256FingerprintRegex();

    [GeneratedRegex(RegistrationTokenPattern)]
    internal static partial Regex RegistrationTokenRegex();

    // ---- Public shape predicates (shared by the Relay and Edge registration flows, SP-07) ----

    /// <summary>
    /// True when <paramref name="value"/> matches gateway.schema.json#/$defs/registrationToken
    /// (<c>hgrt_</c> + base64url body, 48..256 chars). Used by both sides of the registration handshake
    /// (AUTH-FR-01) so a malformed token is rejected before it reaches a store.
    /// </summary>
    public static bool IsRegistrationTokenWellFormed(string? value)
        => value is not null && RegistrationTokenRegex().IsMatch(value);

    // ---- Shared validators (common.schema.json $defs) ----

    /// <summary>Validates a durable ID (common.schema.json#/$defs/id).</summary>
    internal static void Id(string? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "Durable ID is required (common.schema.json#/$defs/id).");
            return;
        }

        if (!IdRegex().IsMatch(value))
        {
            sink.Add(ValidationErrorCodes.InvalidId, path,
                $"'{value}' is not a valid durable ID (8..128 chars, [A-Za-z0-9._:-], no leading separator).");
        }
    }

    /// <summary>Validates an RFC 3339 UTC timestamp (common.schema.json#/$defs/timestamp, incl. format: date-time).</summary>
    internal static void Timestamp(string? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "RFC 3339 UTC timestamp is required (common.schema.json#/$defs/timestamp).");
            return;
        }

        if (!TimestampRegex().IsMatch(value))
        {
            sink.Add(ValidationErrorCodes.InvalidTimestamp, path,
                $"'{value}' is not an RFC 3339 UTC timestamp (yyyy-MM-ddTHH:mm:ss(.fffffffff)?Z).");
            return;
        }

        // The schema also asserts format: date-time; confirm the value is a real UTC date-time.
        // DateTimeOffset has 100 ns precision, so fractions beyond 7 digits are truncated before parsing.
        var candidate = value;
        var dot = candidate.IndexOf('.');
        if (dot > 0 && candidate.Length - dot - 1 > 7)
        {
            candidate = candidate[..(dot + 8)];
        }

        if (!DateTimeOffset.TryParse(
                candidate, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _))
        {
            sink.Add(ValidationErrorCodes.InvalidTimestamp, path,
                $"'{value}' is not a valid RFC 3339 date-time.");
        }
    }

    /// <summary>Validates a content hash (common.schema.json#/$defs/contentHash).</summary>
    internal static void ContentHash(string? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "Content hash is required (sha256:<64 hex>).");
            return;
        }

        if (!ContentHashRegex().IsMatch(value))
        {
            sink.Add(ValidationErrorCodes.InvalidContentHash, path,
                $"'{value}' is not a valid content hash — expected sha256:<64 lowercase hex> (common.schema.json#/$defs/contentHash).");
        }
    }

    /// <summary>Validates a typed participant address (common.schema.json#/$defs/participantAddress, PROTO-FR-02).</summary>
    internal static void ParticipantAddress(string? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "Participant address is required (common.schema.json#/$defs/participantAddress).");
            return;
        }

        if (!ParticipantAddressRegex().IsMatch(value))
        {
            sink.Add(ValidationErrorCodes.InvalidAddress, path,
                $"'{value}' is not a typed participant address — expected (human|agent|system):suffix (PROTO-FR-02).");
        }
    }

    /// <summary>Validates a sync cursor; null means 'no position yet' and is allowed (syncbatch.schema.json#/$defs/cursor).</summary>
    internal static void Cursor(string? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length is < 1 or > 1024 || !CursorRegex().IsMatch(value))
        {
            sink.Add(ValidationErrorCodes.InvalidCursor, path,
                "Cursor must be an opaque URL-safe token of 1..1024 chars ([A-Za-z0-9._:/-]) or null (syncbatch.schema.json#/$defs/cursor).");
        }
    }

    /// <summary>Validates a sync-batch idempotency key (syncbatch.schema.json#/idempotencyKey, SYNC-FR-02).</summary>
    internal static void IdempotencyKey(string? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "Idempotency key is required (syncbatch.schema.json#/idempotencyKey).");
            return;
        }

        if (value.Length is < 1 or > 128 || !IdempotencyKeyRegex().IsMatch(value))
        {
            sink.Add(ValidationErrorCodes.InvalidPattern, path,
                "Idempotency key must be 1..128 chars of [A-Za-z0-9._:-].");
        }
    }

    /// <summary>Validates correlation tokens: an object mapping string keys to string values (common.schema.json#/$defs/correlationTokens).</summary>
    internal static void CorrelationTokens(Dictionary<string, string>? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            return; // optional
        }

        // The Dictionary<string, string> type enforces string→string at deserialization; the schema's
        // additionalProperties: { type: string } is thereby honoured at the wire boundary.
        foreach (var (key, _) in value)
        {
            if (string.IsNullOrEmpty(key))
            {
                sink.Add(ValidationErrorCodes.InvalidPattern, $"{path}",
                    "Correlation token keys must be non-empty strings.");
                break;
            }
        }
    }

    // ---- Generic field helpers (mirroring min/maxLength and minimum/maximum) ----

    /// <summary>Validates an optional-or-required string field against its min/max length bounds.</summary>
    internal static void Text(string? value, string path, ErrorSink sink, bool required, int minLength, int maxLength, string field)
    {
        if (value is null)
        {
            if (required)
            {
                sink.Add(ValidationErrorCodes.Required, path, $"{field} is required.");
            }
            return;
        }

        if (value.Length < minLength || value.Length > maxLength)
        {
            sink.Add(ValidationErrorCodes.InvalidLength, path,
                $"{field} must be {minLength}..{maxLength} characters.");
        }
    }

    /// <summary>Validates an optional string field against a required pattern.</summary>
    internal static void Pattern(string? value, string path, ErrorSink sink, Regex regex, string field)
    {
        if (value is null)
        {
            return;
        }

        if (!regex.IsMatch(value))
        {
            sink.Add(ValidationErrorCodes.InvalidPattern, path, $"{field} does not match the required pattern '{regex}'.");
        }
    }

    /// <summary>
    /// Validates a schema-required string field against a required pattern (mirrors the schema's
    /// <c>required</c> + <c>pattern</c> pair; null yields <see cref="ValidationErrorCodes.Required"/>).
    /// </summary>
    internal static void RequiredPattern(string? value, string path, ErrorSink sink, Regex regex, string field)
    {
        if (value is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, $"{field} is required.");
            return;
        }

        if (!regex.IsMatch(value))
        {
            sink.Add(ValidationErrorCodes.InvalidPattern, path, $"{field} does not match the required pattern '{regex}'.");
        }
    }

    /// <summary>Validates an integer field against min/max bounds.</summary>
    internal static void Range(long value, string path, ErrorSink sink, long min, long max, string field)
    {
        if (value < min || value > max)
        {
            sink.Add(ValidationErrorCodes.OutOfRange, path, $"{field} must be {min}..{max}.");
        }
    }

    /// <summary>Validates an optional free-form JSON value is an object (schema properties of type object).</summary>
    internal static void JsonObject(JsonElement? value, string path, ErrorSink sink, string field)
    {
        if (value is not { } element)
        {
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            sink.Add(ValidationErrorCodes.InvalidJsonValue, path, $"{field} must be a JSON object.");
        }
    }
}
