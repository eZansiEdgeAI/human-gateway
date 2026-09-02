using HumanGateway.Core.Hashing;
using HumanGateway.Core.Ids;
using HumanGateway.Core.Time;
using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Api;
using HumanGateway.Relay.Services;
using HumanGateway.Security;

namespace HumanGateway.Relay.Endpoints;

/// <summary>
/// Remote message ingress. It deliberately has no Edge callback or websocket: the Relay queues the envelope
/// for the recipient gateway and that gateway obtains it on its next outbound sync pull (SP-01).
/// </summary>
public static class RemoteMessageEndpoints
{
    public static void MapRemoteMessageEndpoints(this WebApplication app)
    {
        app.MapPost("/remote/messages", static async (
            RemoteMessageRequest request,
            HttpContext http,
            RelaySyncService sync,
            CancellationToken ct) =>
        {
            var user = CurrentUser.TryGetCurrentUser(http);
            if (user is null)
            {
                return Results.Json(new ProtocolError
                {
                    Code = ErrorCodes.Unauthorized,
                    Message = "A valid session is required to submit a remote message.",
                    Retryable = false,
                }, statusCode: StatusCodes.Status401Unauthorized);
            }
            ArgumentNullException.ThrowIfNull(request);

            var now = ProtocolTime.Now();
            var message = new Message
            {
                Id = IdGenerator.NewId(),
                Sender = request.Sender,
                Recipients = request.Recipients.ToList(),
                ConversationId = request.ConversationId,
                ReplyToMessageId = request.ReplyToMessageId,
                WorkflowRef = request.WorkflowRef,
                HumanTaskId = request.HumanTaskId,
                Payload = request.Payload,
                ArtifactRefs = request.ArtifactRefs?.ToList(),
                CorrelationTokens = request.CorrelationTokens,
                CreatedAt = now,
                UpdatedAt = now,
                ContentHash = null!,
            };
            message = message with { ContentHash = ContentHasher.ComputeMessageHash(message) };
            await sync.PublishRemoteMessageAsync(message, user, ct);
            return Results.Created($"/messages/{message.Id}", message);
        });
    }
}
