using HumanGateway.Protocol.Models;

namespace HumanGateway.Protocol.Validation;

/// <summary>
/// Validates a <see cref="HumanTask"/> against humantask.schema.json (FLOW-FR-04, FLOW-FR-05): task kind
/// and lifecycle status, workflow correlation fields, the response shape (approval responses require a
/// decision), and per-status snapshot invariants.
/// </summary>
public sealed class HumanTaskValidator : IProtocolValidator<HumanTask>
{
    internal static readonly HumanTaskValidator Instance = new();

    /// <inheritdoc />
    public ProtocolValidationResult Validate(HumanTask? value)
    {
        if (value is null)
        {
            return ProtocolValidationResult.Invalid(
                new ProtocolValidationError(ValidationErrorCodes.Required, "#", "HumanTask is required (humantask.schema.json)."));
        }

        var sink = new ErrorSink();
        ValidateInto(value, "", sink);
        return sink.ToResult();
    }

    internal void ValidateInto(HumanTask? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "HumanTask is required.");
            return;
        }

        CommonRules.Id(value.Id, $"{path}id", sink);

        HumanTaskKind? kind = null;
        if (value.Kind is not { } k)
        {
            sink.Add(ValidationErrorCodes.Required, $"{path}kind", "Task kind is required (input|approval).");
        }
        else if (!Enum.IsDefined(k))
        {
            sink.Add(ValidationErrorCodes.UndefinedEnum, $"{path}kind", $"'{k}' is not a defined task kind.");
        }
        else
        {
            kind = k;
        }

        HumanTaskStatus? status = null;
        if (value.Status is not { } s)
        {
            sink.Add(ValidationErrorCodes.Required, $"{path}status", "Task status is required (REQUESTED|DELIVERED_TO_HUMAN|RESPONSE_RECEIVED|COMPLETED|EXPIRED).");
        }
        else if (!Enum.IsDefined(s))
        {
            sink.Add(ValidationErrorCodes.UndefinedEnum, $"{path}status", $"'{s}' is not a defined task status.");
        }
        else
        {
            status = s;
        }

        CommonRules.Text(value.WorkflowRef, $"{path}workflowRef", sink, true, 1, 255, "workflowRef");
        CommonRules.Text(value.NodeId, $"{path}nodeId", sink, true, 1, 255, "nodeId");
        CommonRules.Text(value.Role, $"{path}role", sink, false, 1, 255, "role");
        CommonRules.Text(value.Prompt, $"{path}prompt", sink, true, 1, CommonRules.MaxPromptLength, "prompt");
        CommonRules.Text(value.Subject, $"{path}subject", sink, false, 0, 255, "subject");

        if (value.Options is not null)
        {
            if (value.Options.Count > 100)
            {
                sink.Add(ValidationErrorCodes.MaxItems, $"{path}options", "options must contain at most 100 entries.");
            }
            for (var i = 0; i < value.Options.Count; i++)
            {
                CommonRules.Text(value.Options[i], $"{path}options[{i}]", sink, true, 0, 1024, "options");
            }
        }

        CommonRules.Id(value.RequestMessageId, $"{path}requestMessageId", sink);
        if (value.ResponseMessageId is not null)
        {
            CommonRules.Id(value.ResponseMessageId, $"{path}responseMessageId", sink);
        }

        if (value.Response is { } response)
        {
            ValidateResponseInto(response, $"{path}response", sink, kind);
        }

        if (value.CorrelationToken is not null)
        {
            CommonRules.Text(value.CorrelationToken, $"{path}correlationToken", sink, false, 1, 4096, "correlationToken");
        }

        if (value.ExpiresAt is not null)
        {
            CommonRules.Timestamp(value.ExpiresAt, $"{path}expiresAt", sink);
        }
        if (value.RequestedAt is not null)
        {
            CommonRules.Timestamp(value.RequestedAt, $"{path}requestedAt", sink);
        }
        if (value.DeliveredToHumanAt is not null)
        {
            CommonRules.Timestamp(value.DeliveredToHumanAt, $"{path}deliveredToHumanAt", sink);
        }
        if (value.ResponseReceivedAt is not null)
        {
            CommonRules.Timestamp(value.ResponseReceivedAt, $"{path}responseReceivedAt", sink);
        }
        if (value.CompletedAt is not null)
        {
            CommonRules.Timestamp(value.CompletedAt, $"{path}completedAt", sink);
        }
        if (value.ExpiredAt is not null)
        {
            CommonRules.Timestamp(value.ExpiredAt, $"{path}expiredAt", sink);
        }

        CommonRules.Timestamp(value.CreatedAt, $"{path}createdAt", sink);
        if (value.UpdatedAt is not null)
        {
            CommonRules.Timestamp(value.UpdatedAt, $"{path}updatedAt", sink);
        }

        // Snapshot invariants (humantask.schema.json allOf).
        if (status is { } st)
        {
            switch (st)
            {
                case HumanTaskStatus.Requested:
                    sink.Require(value.RequestedAt is not null, ValidationErrorCodes.StateTimestampRequired, $"{path}requestedAt",
                        "REQUESTED tasks must record requestedAt.");
                    break;
                case HumanTaskStatus.DeliveredToHuman:
                    sink.Require(value.DeliveredToHumanAt is not null, ValidationErrorCodes.StateTimestampRequired, $"{path}deliveredToHumanAt",
                        "DELIVERED_TO_HUMAN tasks must record deliveredToHumanAt.");
                    break;
                case HumanTaskStatus.ResponseReceived:
                    sink.Require(value.Response is not null, ValidationErrorCodes.TaskResponseRequired, $"{path}response",
                        "RESPONSE_RECEIVED tasks must carry the response.");
                    sink.Require(value.ResponseMessageId is not null, ValidationErrorCodes.TaskResponseRequired, $"{path}responseMessageId",
                        "RESPONSE_RECEIVED tasks must record responseMessageId.");
                    sink.Require(value.ResponseReceivedAt is not null, ValidationErrorCodes.StateTimestampRequired, $"{path}responseReceivedAt",
                        "RESPONSE_RECEIVED tasks must record responseReceivedAt.");
                    break;
                case HumanTaskStatus.Completed:
                    sink.Require(value.Response is not null, ValidationErrorCodes.TaskResponseRequired, $"{path}response",
                        "COMPLETED tasks must carry the response.");
                    sink.Require(value.ResponseMessageId is not null, ValidationErrorCodes.TaskResponseRequired, $"{path}responseMessageId",
                        "COMPLETED tasks must record responseMessageId.");
                    sink.Require(value.CompletedAt is not null, ValidationErrorCodes.StateTimestampRequired, $"{path}completedAt",
                        "COMPLETED tasks must record completedAt.");
                    break;
                case HumanTaskStatus.Expired:
                    sink.Require(value.ExpiredAt is not null, ValidationErrorCodes.StateTimestampRequired, $"{path}expiredAt",
                        "EXPIRED tasks must record expiredAt.");
                    break;
            }
        }
    }

    private static void ValidateResponseInto(TaskResponse response, string path, ErrorSink sink, HumanTaskKind? kind)
    {
        CommonRules.Text(response.Text, $"{path}text", sink, false, 0, CommonRules.MaxPromptLength, "response.text");
        if (response.Decision is { } decision && !Enum.IsDefined(decision))
        {
            sink.Add(ValidationErrorCodes.UndefinedEnum, $"{path}decision", $"'{decision}' is not a defined approval decision.");
        }
        CommonRules.Text(response.Reason, $"{path}reason", sink, false, 0, CommonRules.MaxPromptLength, "response.reason");

        if (response.ArtifactRefs is not null)
        {
            for (var i = 0; i < response.ArtifactRefs.Count; i++)
            {
                ArtifactValidator.Instance.ValidateReferenceInto(response.ArtifactRefs[i], $"{path}artifactRefs[{i}]", sink);
            }
        }

        if (response.RespondedBy is not null)
        {
            ParticipantValidator.Instance.ValidateInto(response.RespondedBy, $"{path}respondedBy", sink);
        }
        if (response.RespondedAt is not null)
        {
            CommonRules.Timestamp(response.RespondedAt, $"{path}respondedAt", sink);
        }

        // allOf: an approval task, once answered, must carry a decision.
        if (kind == HumanTaskKind.Approval && response.Decision is null)
        {
            sink.Add(ValidationErrorCodes.ApprovalDecisionRequired, $"{path}decision",
                "Approval task responses must carry a decision (approved|rejected) — humantask.schema.json allOf.");
        }
    }
}
