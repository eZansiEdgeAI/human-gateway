namespace HumanGateway.Protocol.Validation;

/// <summary>Stable, machine-readable validation error codes emitted by the entity validators.</summary>
public static class ValidationErrorCodes
{
    /// <summary>Aggregate protocol error code (reserved catalog, error.schema.json): entity validation failed.</summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>A schema-required field is missing or null.</summary>
    public const string Required = "REQUIRED";
    /// <summary>Value does not match common.schema.json#/$defs/id.</summary>
    public const string InvalidId = "INVALID_ID";
    /// <summary>Value does not match common.schema.json#/$defs/timestamp (RFC 3339 UTC).</summary>
    public const string InvalidTimestamp = "INVALID_TIMESTAMP";
    /// <summary>Value does not match common.schema.json#/$defs/contentHash (sha256:&lt;64 hex&gt;).</summary>
    public const string InvalidContentHash = "INVALID_CONTENT_HASH";
    /// <summary>Value does not match common.schema.json#/$defs/participantAddress.</summary>
    public const string InvalidAddress = "INVALID_ADDRESS";
    /// <summary>Participant address prefix does not agree with its kind (participant.schema.json anyOf).</summary>
    public const string AddressKindMismatch = "ADDRESS_KIND_MISMATCH";
    /// <summary>Enum field holds an undefined value.</summary>
    public const string UndefinedEnum = "UNDEFINED_ENUM";
    /// <summary>String field violates its min/max length.</summary>
    public const string InvalidLength = "INVALID_LENGTH";
    /// <summary>String field violates its schema pattern.</summary>
    public const string InvalidPattern = "INVALID_PATTERN";
    /// <summary>Numeric field is outside its schema bounds.</summary>
    public const string OutOfRange = "OUT_OF_RANGE";
    /// <summary>Array has fewer than minItems elements.</summary>
    public const string MinItems = "MIN_ITEMS";
    /// <summary>Array has more than maxItems elements.</summary>
    public const string MaxItems = "MAX_ITEMS";
    /// <summary>Cursor violates syncbatch.schema.json#/$defs/cursor (≤ 1024 URL-safe chars).</summary>
    public const string InvalidCursor = "INVALID_CURSOR";
    /// <summary>A field is present where the schema forbids it (syncbatch.schema.json allOf: an empty
    /// keepalive batch must leave sequenceStart/sequenceEnd null).</summary>
    public const string UnexpectedValue = "UNEXPECTED_VALUE";
    /// <summary>Sync item payload does not match its kind discriminator (oneOf).</summary>
    public const string ItemKindMismatch = "ITEM_KIND_MISMATCH";
    /// <summary>Delivery state requires a state-specific timestamp (delivery.schema.json allOf).</summary>
    public const string StateTimestampRequired = "STATE_TIMESTAMP_REQUIRED";
    /// <summary>Delivery FAILED requires an error (delivery.schema.json allOf).</summary>
    public const string StateErrorRequired = "STATE_ERROR_REQUIRED";
    /// <summary>Approval task with a response requires a decision (humantask.schema.json allOf).</summary>
    public const string ApprovalDecisionRequired = "APPROVAL_DECISION_REQUIRED";
    /// <summary>Task status requires response fields (humantask.schema.json allOf).</summary>
    public const string TaskResponseRequired = "TASK_RESPONSE_REQUIRED";
    /// <summary>Value must be a JSON object (schema property of type object).</summary>
    public const string InvalidJsonValue = "INVALID_JSON_VALUE";
}
