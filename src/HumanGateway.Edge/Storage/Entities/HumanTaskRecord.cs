using HumanGateway.Protocol.Models;

namespace HumanGateway.Edge.Storage.Entities;

/// <summary>
/// Durable local storage of a protocol <see cref="HumanTask"/> (FLOW-FR-04/05, PWA-FR-06). The full envelope is
/// kept as canonical JSON in <see cref="Envelope"/>; the scalar columns are denormalised for indexed querying
/// (list by status, correlate to the request/response messages).
/// </summary>
/// <remarks>
/// Tasks are a <em>local-store</em> concept: the v1 sync protocol transports tasks <em>inside</em> message
/// envelopes (there is no task sync item), so the task record lives only on the gateway that created/answered
/// it. The request message (<see cref="RequestMessageId"/>) carries the task out to recipients.
/// </remarks>
public sealed class HumanTaskRecord
{
    /// <summary>Durable human task ID — the primary key.</summary>
    public string Id { get; set; } = null!;

    /// <summary>Wire-token lifecycle state (<c>REQUESTED</c>, <c>DELIVERED_TO_HUMAN</c>, ...) for filtering.</summary>
    public string Status { get; set; } = null!;

    /// <summary>Wire-token task kind (<c>input</c> | <c>approval</c>) for filtering.</summary>
    public string? Kind { get; set; }

    /// <summary>Consumer workflow/run identifier (FLOW-FR-05), indexed for correlation.</summary>
    public string WorkflowRef { get; set; } = null!;

    /// <summary>The message envelope carrying the task request.</summary>
    public string RequestMessageId { get; set; } = null!;

    /// <summary>The message envelope carrying the human's response, once answered.</summary>
    public string? ResponseMessageId { get; set; }

    /// <summary>The full protocol task, stored as canonical wire JSON.</summary>
    public HumanTask Envelope { get; set; } = null!;

    /// <summary>Creates a storage record from a protocol task, deriving the query columns.</summary>
    public static HumanTaskRecord FromEnvelope(HumanTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new HumanTaskRecord
        {
            Id = task.Id,
            Status = ProtocolJsonConversions.WireToken(task.Status) ?? string.Empty,
            Kind = ProtocolJsonConversions.WireToken(task.Kind),
            WorkflowRef = task.WorkflowRef,
            RequestMessageId = task.RequestMessageId,
            ResponseMessageId = task.ResponseMessageId,
            Envelope = task,
        };
    }
}
