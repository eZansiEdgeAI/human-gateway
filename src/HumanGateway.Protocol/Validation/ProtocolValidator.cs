namespace HumanGateway.Protocol.Validation;

/// <summary>
/// Composite entry point for protocol validation: exposes a validator per protocol entity. Validators are
/// stateless singletons; the composite wires the nested dependencies (e.g. a Message validates its
/// Participants and ArtifactReferences through the shared validators).
/// </summary>
public sealed class ProtocolValidator
{
    /// <summary>The shared default instance.</summary>
    public static ProtocolValidator Default { get; } = new();

    /// <summary>Error entity validator (error.schema.json).</summary>
    public ErrorValidator Error { get; } = new();

    /// <summary>Participant entity validator (participant.schema.json, PROTO-FR-02).</summary>
    public ParticipantValidator Participant { get; } = new();

    /// <summary>Artifact entity validator (artifact.schema.json, PROTO-FR-04).</summary>
    public ArtifactValidator Artifact { get; } = new();

    /// <summary>Message entity validator (message.schema.json, PROTO-FR-03).</summary>
    public MessageValidator Message { get; } = new();

    /// <summary>Delivery entity validator (delivery.schema.json, PROTO-FR-05).</summary>
    public DeliveryValidator Delivery { get; } = new();

    /// <summary>HumanTask entity validator (humantask.schema.json, FLOW-FR-04/05).</summary>
    public HumanTaskValidator HumanTask { get; } = new();

    /// <summary>SyncBatch entity validator (syncbatch.schema.json, SYNC-FR-01..07).</summary>
    public SyncBatchValidator SyncBatch { get; } = new();

    /// <summary>Gateway identity validator (gateway.schema.json, AUTH-FR-01).</summary>
    public GatewayValidator Gateway { get; } = new();

    /// <summary>User account validator (user.schema.json, AUTH-FR-02).</summary>
    public UserValidator User { get; } = new();
}
