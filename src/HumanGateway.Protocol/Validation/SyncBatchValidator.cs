using HumanGateway.Protocol.Models;

namespace HumanGateway.Protocol.Validation;

/// <summary>
/// Validates a <see cref="SyncBatch"/> against syncbatch.schema.json (SYNC-FR-01..07): the sync model —
/// durable IDs, per-gateway sequence numbers, opaque cursors, idempotency (batchId + idempotencyKey),
/// content hashes — plus the discriminated sync items and the batch-shape invariants (a non-empty batch
/// declares its sequence range; an empty keepalive batch leaves it null). Cross-field range checks
/// (sequenceStart ≤ sequenceEnd, item sequences within the span) are enforced by the sync engine, not here
/// (schemas/README.md).
/// </summary>
public sealed class SyncBatchValidator : IProtocolValidator<SyncBatch>
{
    internal static readonly SyncBatchValidator Instance = new();

    /// <inheritdoc />
    public ProtocolValidationResult Validate(SyncBatch? value)
    {
        if (value is null)
        {
            return ProtocolValidationResult.Invalid(
                new ProtocolValidationError(ValidationErrorCodes.Required, "#", "SyncBatch is required (syncbatch.schema.json)."));
        }

        var sink = new ErrorSink();
        ValidateInto(value, "", sink);
        return sink.ToResult();
    }

    internal void ValidateInto(SyncBatch? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "SyncBatch is required.");
            return;
        }

        CommonRules.Id(value.BatchId, $"{path}batchId", sink);
        CommonRules.Id(value.GatewayId, $"{path}gatewayId", sink);

        if (value.Direction is not { } direction)
        {
            sink.Add(ValidationErrorCodes.Required, $"{path}direction", "Batch direction is required (PUSH|PULL).");
        }
        else if (!Enum.IsDefined(direction))
        {
            sink.Add(ValidationErrorCodes.UndefinedEnum, $"{path}direction", $"'{direction}' is not a defined batch direction.");
        }

        CommonRules.Cursor(value.SinceCursor, $"{path}sinceCursor", sink);
        CommonRules.Cursor(value.Cursor, $"{path}cursor", sink);
        CommonRules.IdempotencyKey(value.IdempotencyKey, $"{path}idempotencyKey", sink);

        if (value.SequenceStart is { } sequenceStart)
        {
            CommonRules.Range(sequenceStart, $"{path}sequenceStart", sink, 1, long.MaxValue, "sequenceStart");
        }
        if (value.SequenceEnd is { } sequenceEnd)
        {
            CommonRules.Range(sequenceEnd, $"{path}sequenceEnd", sink, 1, long.MaxValue, "sequenceEnd");
        }

        if (value.Items is null)
        {
            sink.Add(ValidationErrorCodes.Required, $"{path}items", "items is required (may be an empty keepalive batch).");
        }
        else
        {
            if (value.Items.Count > CommonRules.MaxSyncItemsPerBatch)
            {
                sink.Add(ValidationErrorCodes.MaxItems, $"{path}items",
                    $"items must contain at most {CommonRules.MaxSyncItemsPerBatch} entries per batch.");
            }

            for (var i = 0; i < value.Items.Count; i++)
            {
                ValidateItemInto(value.Items[i], $"{path}items[{i}]", sink);
            }
        }

        // Batch-shape invariant (syncbatch.schema.json allOf): non-empty items require the sequence range;
        // an empty (keepalive) batch must leave both null.
        var hasItems = value.Items is { Count: > 0 };
        var hasRange = value.SequenceStart is not null && value.SequenceEnd is not null;
        if (hasItems && !hasRange)
        {
            sink.Add(ValidationErrorCodes.Required, $"{path}sequenceStart",
                "A non-empty batch must declare its sequence range (sequenceStart and sequenceEnd).");
        }
        if (!hasItems && hasRange)
        {
            sink.Add(ValidationErrorCodes.UnexpectedValue, $"{path}sequenceStart",
                "An empty keepalive batch must leave sequenceStart and sequenceEnd null.");
        }

        CommonRules.Timestamp(value.CreatedAt, $"{path}createdAt", sink);
    }

    private static void ValidateItemInto(SyncItem? item, string path, ErrorSink sink)
    {
        if (item is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "Sync item is required.");
            return;
        }

        if (item.Kind is not { } kind)
        {
            sink.Add(ValidationErrorCodes.Required, $"{path}kind", "Sync item kind is required (message|delivery|artifact|ack).");
            return;
        }

        if (!Enum.IsDefined(kind))
        {
            sink.Add(ValidationErrorCodes.UndefinedEnum, $"{path}kind", $"'{kind}' is not a defined sync item kind.");
            return;
        }

        CommonRules.Range(item.Sequence, $"{path}sequence", sink, 1, long.MaxValue, "sequence");

        // oneOf: the sync item must carry exactly the payload matching its kind discriminator.
        var exactlyOnePayload =
            (kind == SyncItemKind.Message && item.Message is not null && item.Delivery is null && item.Artifact is null && item.Ack is null) ||
            (kind == SyncItemKind.Delivery && item.Delivery is not null && item.Message is null && item.Artifact is null && item.Ack is null) ||
            (kind == SyncItemKind.Artifact && item.Artifact is not null && item.Message is null && item.Delivery is null && item.Ack is null) ||
            (kind == SyncItemKind.Ack && item.Ack is not null && item.Message is null && item.Delivery is null && item.Artifact is null);

        if (!exactlyOnePayload)
        {
            sink.Add(ValidationErrorCodes.ItemKindMismatch, path,
                $"A '{kind}' sync item must carry exactly the '{kind}' payload and no other (syncbatch.schema.json#/$defs/syncItem oneOf).");
        }

        switch (kind)
        {
            case SyncItemKind.Message:
                MessageValidator.Instance.ValidateInto(item.Message, $"{path}message", sink);
                break;
            case SyncItemKind.Delivery:
                DeliveryValidator.Instance.ValidateInto(item.Delivery, $"{path}delivery", sink);
                break;
            case SyncItemKind.Artifact:
                ArtifactValidator.Instance.ValidateInto(item.Artifact, $"{path}artifact", sink);
                break;
            case SyncItemKind.Ack:
                ValidateAckInto(item.Ack, $"{path}ack", sink);
                break;
        }
    }

    private static void ValidateAckInto(DeliveryAck? ack, string path, ErrorSink sink)
    {
        if (ack is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "Delivery acknowledgement is required.");
            return;
        }

        CommonRules.Id(ack.MessageId, $"{path}messageId", sink);
        ParticipantValidator.Instance.ValidateInto(ack.Recipient, $"{path}recipient", sink);

        if (ack.State is not { } state)
        {
            sink.Add(ValidationErrorCodes.Required, $"{path}state", "Acknowledgement state is required (DELIVERED|ACKNOWLEDGED|FAILED).");
        }
        else if (!Enum.IsDefined(state))
        {
            sink.Add(ValidationErrorCodes.UndefinedEnum, $"{path}state", $"'{state}' is not a defined acknowledgement state.");
        }

        CommonRules.Timestamp(ack.AcknowledgedAt, $"{path}acknowledgedAt", sink);
    }
}
