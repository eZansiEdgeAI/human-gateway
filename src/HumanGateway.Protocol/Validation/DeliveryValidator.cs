using HumanGateway.Protocol.Models;

namespace HumanGateway.Protocol.Validation;

/// <summary>
/// Validates a <see cref="Delivery"/> against delivery.schema.json (PROTO-FR-05): the delivery state
/// machine's snapshot invariants — every state records the timestamp when it was entered, and FAILED
/// carries error details. WAITING_FOR_SYNC is a valid state, not an error. Transition legality is
/// enforced by the sync engine, not here.
/// </summary>
public sealed class DeliveryValidator : IProtocolValidator<Delivery>
{
    internal static readonly DeliveryValidator Instance = new();

    /// <inheritdoc />
    public ProtocolValidationResult Validate(Delivery? value)
    {
        if (value is null)
        {
            return ProtocolValidationResult.Invalid(
                new ProtocolValidationError(ValidationErrorCodes.Required, "#", "Delivery is required (delivery.schema.json)."));
        }

        var sink = new ErrorSink();
        ValidateInto(value, "", sink);
        return sink.ToResult();
    }

    internal void ValidateInto(Delivery? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "Delivery is required.");
            return;
        }

        CommonRules.Id(value.Id, $"{path}id", sink);
        CommonRules.Id(value.MessageId, $"{path}messageId", sink);
        ParticipantValidator.Instance.ValidateInto(value.Recipient, $"{path}recipient", sink);

        DeliveryState? state = null;
        if (value.State is not { } s)
        {
            sink.Add(ValidationErrorCodes.Required, $"{path}state", "Delivery state is required (QUEUED|SYNCING|DELIVERED|ACKNOWLEDGED|WAITING_FOR_SYNC|FAILED).");
        }
        else if (!Enum.IsDefined(s))
        {
            sink.Add(ValidationErrorCodes.UndefinedEnum, $"{path}state", $"'{s}' is not a defined delivery state.");
        }
        else
        {
            state = s;
        }

        CommonRules.Range(value.Attempts, $"{path}attempts", sink, 0, long.MaxValue, "attempts");
        CommonRules.Range(value.MaxAttempts, $"{path}maxAttempts", sink, 1, long.MaxValue, "maxAttempts");

        if (value.NextRetryAt is not null)
        {
            CommonRules.Timestamp(value.NextRetryAt, $"{path}nextRetryAt", sink);
        }
        if (value.QueuedAt is not null)
        {
            CommonRules.Timestamp(value.QueuedAt, $"{path}queuedAt", sink);
        }
        if (value.SyncingAt is not null)
        {
            CommonRules.Timestamp(value.SyncingAt, $"{path}syncingAt", sink);
        }
        if (value.WaitingForSyncAt is not null)
        {
            CommonRules.Timestamp(value.WaitingForSyncAt, $"{path}waitingForSyncAt", sink);
        }
        if (value.DeliveredAt is not null)
        {
            CommonRules.Timestamp(value.DeliveredAt, $"{path}deliveredAt", sink);
        }
        if (value.AcknowledgedAt is not null)
        {
            CommonRules.Timestamp(value.AcknowledgedAt, $"{path}acknowledgedAt", sink);
        }
        if (value.FailedAt is not null)
        {
            CommonRules.Timestamp(value.FailedAt, $"{path}failedAt", sink);
        }
        if (value.Error is not null)
        {
            ErrorValidator.Instance.ValidateInto(value.Error, $"{path}error", sink);
        }

        CommonRules.Timestamp(value.CreatedAt, $"{path}createdAt", sink);
        CommonRules.Timestamp(value.UpdatedAt, $"{path}updatedAt", sink);

        // Snapshot invariants (delivery.schema.json allOf): each state records when it was entered.
        if (state is { } st)
        {
            switch (st)
            {
                case DeliveryState.Queued:
                    sink.Require(value.QueuedAt is not null, ValidationErrorCodes.StateTimestampRequired, $"{path}queuedAt",
                        "QUEUED deliveries must record queuedAt.");
                    break;
                case DeliveryState.WaitingForSync:
                    sink.Require(value.WaitingForSyncAt is not null, ValidationErrorCodes.StateTimestampRequired, $"{path}waitingForSyncAt",
                        "WAITING_FOR_SYNC deliveries must record waitingForSyncAt (offline deferral is a valid state, not an error).");
                    break;
                case DeliveryState.Delivered:
                    sink.Require(value.DeliveredAt is not null, ValidationErrorCodes.StateTimestampRequired, $"{path}deliveredAt",
                        "DELIVERED deliveries must record deliveredAt.");
                    break;
                case DeliveryState.Acknowledged:
                    sink.Require(value.AcknowledgedAt is not null, ValidationErrorCodes.StateTimestampRequired, $"{path}acknowledgedAt",
                        "ACKNOWLEDGED deliveries must record acknowledgedAt.");
                    break;
                case DeliveryState.Failed:
                    sink.Require(value.FailedAt is not null, ValidationErrorCodes.StateTimestampRequired, $"{path}failedAt",
                        "FAILED deliveries must record failedAt.");
                    sink.Require(value.Error is not null, ValidationErrorCodes.StateErrorRequired, $"{path}error",
                        "FAILED deliveries must carry error details (delivery.schema.json allOf).");
                    break;
            }
        }
    }
}
