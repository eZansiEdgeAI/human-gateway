namespace HumanGateway.Protocol;

/// <summary>
/// Versioned <c>$id</c> URIs of the protocol schema documents (release v1.0.0, JSON Schema Draft 2020-12).
/// </summary>
/// <remarks>
/// Every schema document carries one of these as its <c>$id</c>; validators must register every schema
/// under its <c>$id</c> before compiling an entity schema (schemas/README.md). The <c>v1</c> path segment is
/// bumped on release; schemas are immutable once released.
/// </remarks>
public static class ProtocolSchemaIds
{
    /// <summary>Base URI for the current (v1) schema release.</summary>
    public const string BaseUri = "https://schemas.humangateway.dev/human-gateway/v1/";

    public const string Common = BaseUri + "common.schema.json";
    public const string Error = BaseUri + "error.schema.json";
    public const string Gateway = BaseUri + "gateway.schema.json";
    public const string User = BaseUri + "user.schema.json";
    public const string Participant = BaseUri + "participant.schema.json";
    public const string Artifact = BaseUri + "artifact.schema.json";
    public const string Message = BaseUri + "message.schema.json";
    public const string Delivery = BaseUri + "delivery.schema.json";
    public const string HumanTask = BaseUri + "humantask.schema.json";
    public const string SyncBatch = BaseUri + "syncbatch.schema.json";
}
