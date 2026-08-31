namespace HumanGateway.Security;

/// <summary>
/// The protected resource kinds the authorisation middleware can gate (AUTH-FR-03, SP-04):
/// conversations, messages, human tasks, and artifacts. Each maps to the protocol error code
/// reserved for its denial (ErrorCodes.ConversationAccessDenied / TaskAccessDenied /
/// ArtifactAccessDenied) — message access derives from the message's conversation membership,
/// so a message denial reuses <c>CONVERSATION_ACCESS_DENIED</c>.
/// </summary>
public enum AuthorizedResource
{
    /// <summary>A conversation and its message list (membership governs access).</summary>
    Conversation,

    /// <summary>A single message envelope (access via its conversation).</summary>
    Message,

    /// <summary>A human task (access via its conversation or assignment).</summary>
    Task,

    /// <summary>An artifact's metadata and bytes (access via a referencing conversation).</summary>
    Artifact,
}
