using HumanGateway.Protocol.Models;

namespace HumanGateway.Protocol.Validation;

/// <summary>
/// Validates a <see cref="Message"/> against message.schema.json (PROTO-FR-03, PROTO-FR-04): durable
/// envelope shape, typed sender/recipients, payload bounds, artifact references (never embedded bytes),
/// correlation tokens, timestamps, and content hash.
/// </summary>
public sealed class MessageValidator : IProtocolValidator<Message>
{
    internal static readonly MessageValidator Instance = new();

    /// <inheritdoc />
    public ProtocolValidationResult Validate(Message? value)
    {
        if (value is null)
        {
            return ProtocolValidationResult.Invalid(
                new ProtocolValidationError(ValidationErrorCodes.Required, "#", "Message is required (message.schema.json)."));
        }

        var sink = new ErrorSink();
        ValidateInto(value, "", sink);
        return sink.ToResult();
    }

    internal void ValidateInto(Message? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "Message is required.");
            return;
        }

        CommonRules.Id(value.Id, $"{path}id", sink);
        ParticipantValidator.Instance.ValidateInto(value.Sender, $"{path}sender", sink);

        if (value.Recipients is null)
        {
            sink.Add(ValidationErrorCodes.Required, $"{path}recipients", "recipients is required.");
        }
        else
        {
            if (value.Recipients.Count < 1)
            {
                sink.Add(ValidationErrorCodes.MinItems, $"{path}recipients", "recipients must contain at least 1 participant.");
            }
            if (value.Recipients.Count > 64)
            {
                sink.Add(ValidationErrorCodes.MaxItems, $"{path}recipients", "recipients must contain at most 64 participants.");
            }
            for (var i = 0; i < value.Recipients.Count; i++)
            {
                ParticipantValidator.Instance.ValidateInto(value.Recipients[i], $"{path}recipients[{i}]", sink);
            }
        }

        CommonRules.Id(value.ConversationId, $"{path}conversationId", sink);

        if (value.ReplyToMessageId is not null)
        {
            CommonRules.Id(value.ReplyToMessageId, $"{path}replyToMessageId", sink);
        }
        if (value.WorkflowRef is not null)
        {
            CommonRules.Text(value.WorkflowRef, $"{path}workflowRef", sink, false, 1, 255, "workflowRef");
        }
        if (value.HumanTaskId is not null)
        {
            CommonRules.Id(value.HumanTaskId, $"{path}humanTaskId", sink);
        }

        if (value.Payload is null)
        {
            sink.Add(ValidationErrorCodes.Required, $"{path}payload", "payload is required.");
        }
        else
        {
            CommonRules.Text(value.Payload.Body, $"{path}payload.body", sink, true, 0, CommonRules.MaxMessageBodyLength, "payload.body");
            if (value.Payload.Format is { } format && !Enum.IsDefined(format))
            {
                sink.Add(ValidationErrorCodes.UndefinedEnum, $"{path}payload.format", $"'{format}' is not a defined message format.");
            }
            CommonRules.JsonObject(value.Payload.Data, $"{path}payload.data", sink, "payload.data");
        }

        if (value.ArtifactRefs is not null)
        {
            for (var i = 0; i < value.ArtifactRefs.Count; i++)
            {
                ArtifactValidator.Instance.ValidateReferenceInto(value.ArtifactRefs[i], $"{path}artifactRefs[{i}]", sink);
            }
        }

        CommonRules.CorrelationTokens(value.CorrelationTokens, $"{path}correlationTokens", sink);
        CommonRules.Timestamp(value.CreatedAt, $"{path}createdAt", sink);
        if (value.UpdatedAt is not null)
        {
            CommonRules.Timestamp(value.UpdatedAt, $"{path}updatedAt", sink);
        }
        CommonRules.ContentHash(value.ContentHash, $"{path}contentHash", sink);
    }
}
