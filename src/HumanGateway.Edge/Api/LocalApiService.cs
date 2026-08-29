using HumanGateway.Core.Hashing;
using HumanGateway.Core.Ids;
using HumanGateway.Core.Time;
using HumanGateway.Edge.Storage;
using HumanGateway.Edge.Storage.Entities;
using HumanGateway.Protocol.Models;
using HumanGateway.Protocol.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HumanGateway.Edge.Api;

/// <summary>
/// Domain logic behind the Edge local REST API (EDGE-FR-03). Every write is committed to SQLite and, where a
/// message must leave this gateway, enqueued to the durable outbox <em>in the same transaction</em> (EDGE-FR-04:
/// no durable message row without its outbox entry, and no network attempt before the durable write). Methods
/// return <c>null</c> for not-found and throw <see cref="LocalApiException"/> for domain-rule violations; the
/// exception handler wired in <c>Program.cs</c> maps both to HTTP responses.
/// </summary>
public sealed class LocalApiService
{
    private readonly IDbContextFactory<EdgeDbContext> _factory;
    private readonly IOptions<GatewayOptions> _options;

    /// <summary>Creates the service over the durable store factory and gateway options.</summary>
    public LocalApiService(IDbContextFactory<EdgeDbContext> factory, IOptions<GatewayOptions> options)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private string GatewayId => _options.Value.GatewayId;

    // -----------------------------------------------------------------------------------------------
    // Conversations
    // -----------------------------------------------------------------------------------------------

    /// <summary>Lists conversations with membership and activity metadata (message count + last message).</summary>
    public async Task<IReadOnlyList<ConversationView>> ListConversationsAsync(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var conversations = await db.Conversations
            .AsNoTracking()
            .Include(c => c.Participants)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var messageStats = await db.Messages
            .AsNoTracking()
            .GroupBy(m => m.ConversationId)
            .Select(g => new ConversationStats { ConversationId = g.Key, Count = g.Count(), Last = g.Max(m => m.CreatedAt) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var statsByConversation = messageStats.ToDictionary(s => s.ConversationId);
        var directory = await LoadParticipantDirectoryAsync(db, conversations.SelectMany(c => c.Participants).Select(p => p.ParticipantAddress), ct)
            .ConfigureAwait(false);

        return conversations.Select(c => ToConversationView(c, statsByConversation.GetValueOrDefault(c.Id), directory)).ToList();
    }

    /// <summary>Gets one conversation with membership and activity metadata, or null when absent.</summary>
    public async Task<ConversationView?> GetConversationAsync(string id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var conversation = await db.Conversations
            .AsNoTracking()
            .Include(c => c.Participants)
            .SingleOrDefaultAsync(c => c.Id == id, ct)
            .ConfigureAwait(false);
        if (conversation is null)
        {
            return null;
        }

        var stat = await db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == id)
            .GroupBy(m => m.ConversationId)
            .Select(g => new ConversationStats { ConversationId = g.Key, Count = g.Count(), Last = g.Max(m => m.CreatedAt) })
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var directory = await LoadParticipantDirectoryAsync(db, conversation.Participants.Select(p => p.ParticipantAddress), ct)
            .ConfigureAwait(false);

        return ToConversationView(conversation, stat, directory);
    }

    /// <summary>Creates a conversation, upserting its participants into the local directory.</summary>
    public async Task<ConversationView> CreateConversationAsync(CreateConversationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = ProtocolTime.Now();
        var participants = request.Participants.Where(p => p is not null).ToList();
        if (participants.Count == 0)
        {
            throw new LocalApiException(StatusCodes.Status400BadRequest, ErrorCodes.BadRequest, "A conversation requires at least one participant.");
        }

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await UpsertParticipantsAsync(db, participants, ct).ConfigureAwait(false);

        var conversation = new Conversation
        {
            Id = IdGenerator.NewId(),
            Title = request.Title,
            CreatedAt = now,
            UpdatedAt = now,
        };

        foreach (var participant in participants.DistinctBy(p => p.Address))
        {
            conversation.Participants.Add(new ConversationParticipant
            {
                ConversationId = conversation.Id,
                ParticipantAddress = participant.Address,
                JoinedAt = now,
            });
        }

        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ToConversationView(
            conversation,
            null,
            participants.DistinctBy(p => p.Address).ToDictionary(p => p.Address));
    }

    /// <summary>Lists a conversation's messages in chronological order, each with its delivery records.</summary>
    public async Task<IReadOnlyList<MessageView>> ListConversationMessagesAsync(string conversationId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var messages = await db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return await AttachDeliveriesAsync(db, messages, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------------------------------
    // Messages
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Composes and sends a message: validates it, computes its content hash, durably stores it plus a
    /// per-recipient delivery record, and enqueues it to the outbox for relay sync (PWA-FR-04, EDGE-FR-04).
    /// </summary>
    public async Task<MessageView> SendMessageAsync(SendMessageRequest request, CancellationToken ct)
    {
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
        ProtocolValidator.Default.Message.Validate(message).ThrowIfInvalid();

        var deliveries = message.Recipients!
            .Select(r => BuildDelivery(message.Id, r, now))
            .ToList();

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await UpsertParticipantsAsync(db, new[] { message.Sender }.Concat(message.Recipients!), ct).ConfigureAwait(false);
        db.Messages.Add(MessageRecord.FromEnvelope(message));
        foreach (var delivery in deliveries)
        {
            db.Deliveries.Add(DeliveryRecord.FromEnvelope(delivery));
        }

        await AddOutboxEntryAsync(db, GatewayId, message, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new MessageView { Message = message, Deliveries = deliveries };
    }

    /// <summary>Gets a message with its delivery records, or null when absent.</summary>
    public async Task<MessageView?> GetMessageAsync(string id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var message = await db.Messages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct).ConfigureAwait(false);
        if (message is null)
        {
            return null;
        }

        var deliveries = await db.Deliveries
            .AsNoTracking()
            .Where(d => d.MessageId == id)
            .OrderBy(d => d.RecipientAddress)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new MessageView
        {
            Message = message.Envelope,
            Deliveries = deliveries.Select(d => d.Envelope).ToList(),
        };
    }

    // -----------------------------------------------------------------------------------------------
    // Tasks
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Creates a human task (input or approval): stores the task durably and sends the request message carrying
    /// it to the assignees (FLOW-FR-05, PWA-FR-06). The full task content lives in the task record; the request
    /// message carries the prompt plus the <c>humanTaskId</c> correlation for transport.
    /// </summary>
    public async Task<HumanTask> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = ProtocolTime.Now();
        var taskId = IdGenerator.NewId();
        var requestMessageId = IdGenerator.NewId();
        var conversationId = request.ConversationId ?? IdGenerator.NewId();

        var task = new HumanTask
        {
            Id = taskId,
            Kind = request.Kind,
            Status = HumanTaskStatus.Requested,
            WorkflowRef = request.WorkflowRef,
            NodeId = request.NodeId,
            Role = request.Role,
            Prompt = request.Prompt,
            Subject = request.Subject,
            Options = request.Options?.ToList(),
            RequestMessageId = requestMessageId,
            CorrelationToken = request.CorrelationToken,
            ExpiresAt = request.ExpiresAt,
            RequestedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ProtocolValidator.Default.HumanTask.Validate(task).ThrowIfInvalid();

        var message = new Message
        {
            Id = requestMessageId,
            Sender = request.Requester,
            Recipients = request.Assignees.ToList(),
            ConversationId = conversationId,
            HumanTaskId = taskId,
            Payload = new MessagePayload { Body = request.Prompt, Format = MessageFormat.Plaintext },
            CreatedAt = now,
            UpdatedAt = now,
            ContentHash = null!,
        };
        message = message with { ContentHash = ContentHasher.ComputeMessageHash(message) };
        ProtocolValidator.Default.Message.Validate(message).ThrowIfInvalid();

        var deliveries = request.Assignees
            .Select(a => BuildDelivery(requestMessageId, a, now))
            .ToList();

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await UpsertParticipantsAsync(db, new[] { request.Requester }.Concat(request.Assignees), ct).ConfigureAwait(false);
        db.Tasks.Add(HumanTaskRecord.FromEnvelope(task));
        db.Messages.Add(MessageRecord.FromEnvelope(message));
        foreach (var delivery in deliveries)
        {
            db.Deliveries.Add(DeliveryRecord.FromEnvelope(delivery));
        }

        await AddOutboxEntryAsync(db, GatewayId, message, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return task;
    }

    /// <summary>Lists tasks, optionally filtered to a single lifecycle state token (e.g. <c>REQUESTED</c>).</summary>
    public async Task<IReadOnlyList<HumanTask>> ListTasksAsync(string? status, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var query = db.Tasks.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(t => t.Status == status);
        }

        // Ordering by the envelope's requested/created time is not SQL-translatable (the envelope is a
        // value-converted JSON column), so fetch the filtered set and sort in memory — fine for a class-sized
        // local task list (NF-01).
        var records = await query.ToListAsync(ct).ConfigureAwait(false);
        return records
            .OrderByDescending(t => t.Envelope.RequestedAt ?? t.Envelope.CreatedAt)
            .Select(t => t.Envelope)
            .ToList();
    }

    /// <summary>Gets a task by ID, or null when absent.</summary>
    public async Task<HumanTask?> GetTaskAsync(string id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var record = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
        return record?.Envelope;
    }

    /// <summary>
    /// Records the human's answer to a task: updates the task to RESPONSE_RECEIVED and sends the response
    /// message back to the requester (PWA-FR-06, FLOW-FR-05). Returns the updated task, or null when absent.
    /// </summary>
    public async Task<HumanTask?> AnswerTaskAsync(string id, AnswerTaskRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = ProtocolTime.Now();

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var record = await db.Tasks.SingleOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var task = record.Envelope;
        // A task can only be answered once, while it is still open (REQUESTED or DELIVERED_TO_HUMAN).
        if (task.Status is not (HumanTaskStatus.Requested or HumanTaskStatus.DeliveredToHuman))
        {
            throw new LocalApiException(StatusCodes.Status409Conflict, ErrorCodes.Conflict, $"Task {id} is already {task.Status} and can no longer be answered.");
        }

        var requestMessage = await db.Messages
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.Id == task.RequestMessageId, ct)
            .ConfigureAwait(false);
        if (requestMessage is null)
        {
            throw new LocalApiException(StatusCodes.Status500InternalServerError, ErrorCodes.InternalError, $"Request message {task.RequestMessageId} for task {id} is missing.");
        }

        var responseMessageId = IdGenerator.NewId();
        var response = new TaskResponse
        {
            Text = request.Text,
            Decision = request.Decision,
            Reason = request.Reason,
            ArtifactRefs = request.ArtifactRefs?.ToList(),
            RespondedBy = request.RespondedBy,
            RespondedAt = now,
        };

        var updated = task with
        {
            Status = HumanTaskStatus.ResponseReceived,
            Response = response,
            ResponseMessageId = responseMessageId,
            ResponseReceivedAt = now,
            UpdatedAt = now,
        };
        ProtocolValidator.Default.HumanTask.Validate(updated).ThrowIfInvalid();

        var responseMessage = new Message
        {
            Id = responseMessageId,
            Sender = request.RespondedBy,
            Recipients = new List<Participant> { requestMessage.Envelope.Sender },
            ConversationId = requestMessage.ConversationId,
            ReplyToMessageId = task.RequestMessageId,
            HumanTaskId = task.Id,
            Payload = new MessagePayload { Body = BuildResponseBody(request), Format = MessageFormat.Plaintext },
            ArtifactRefs = request.ArtifactRefs?.ToList(),
            CreatedAt = now,
            UpdatedAt = now,
            ContentHash = null!,
        };
        responseMessage = responseMessage with { ContentHash = ContentHasher.ComputeMessageHash(responseMessage) };
        ProtocolValidator.Default.Message.Validate(responseMessage).ThrowIfInvalid();

        var deliveries = responseMessage.Recipients!
            .Select(r => BuildDelivery(responseMessageId, r, now))
            .ToList();

        record.Envelope = updated;
        record.Status = ProtocolJsonConversions.WireToken(updated.Status) ?? string.Empty;
        record.Kind = ProtocolJsonConversions.WireToken(updated.Kind);
        record.ResponseMessageId = responseMessageId;

        await UpsertParticipantsAsync(db, new[] { request.RespondedBy }.Concat(responseMessage.Recipients!), ct).ConfigureAwait(false);
        db.Messages.Add(MessageRecord.FromEnvelope(responseMessage));
        foreach (var delivery in deliveries)
        {
            db.Deliveries.Add(DeliveryRecord.FromEnvelope(delivery));
        }

        await AddOutboxEntryAsync(db, GatewayId, responseMessage, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return updated;
    }

    // -----------------------------------------------------------------------------------------------
    // Artifacts
    // -----------------------------------------------------------------------------------------------

    /// <summary>Registers artifact metadata (bytes land via the filesystem artifact store, LOCAL-EDGE-1.5).</summary>
    public async Task<Artifact> RegisterArtifactAsync(RegisterArtifactRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = ProtocolTime.Now();
        var artifact = new Artifact
        {
            Id = request.Id ?? IdGenerator.NewId(),
            Hash = request.Hash,
            SizeBytes = request.SizeBytes,
            MimeType = request.MimeType,
            Filename = request.Filename,
            Description = request.Description,
            CreatedAt = now,
        };
        ProtocolValidator.Default.Artifact.Validate(artifact).ThrowIfInvalid();

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var exists = await db.Artifacts.AsNoTracking().AnyAsync(a => a.Id == artifact.Id, ct).ConfigureAwait(false);
        if (exists)
        {
            throw new LocalApiException(StatusCodes.Status409Conflict, ErrorCodes.Conflict, $"Artifact {artifact.Id} already exists.");
        }

        db.Artifacts.Add(ArtifactRecord.FromEnvelope(artifact));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return artifact;
    }

    /// <summary>Lists artifact metadata records.</summary>
    public async Task<IReadOnlyList<Artifact>> ListArtifactsAsync(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        return (await db.Artifacts.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct).ConfigureAwait(false))
            .Select(a => a.Envelope)
            .ToList();
    }

    /// <summary>Gets artifact metadata by ID, or null when absent.</summary>
    public async Task<Artifact?> GetArtifactAsync(string id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var record = await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false);
        return record?.Envelope;
    }

    // -----------------------------------------------------------------------------------------------
    // Sync status
    // -----------------------------------------------------------------------------------------------

    /// <summary>Builds the sync-status snapshot for the PWA sync banner (EDGE-FR-05).</summary>
    public async Task<SyncStatusView> GetSyncStatusAsync(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var queued = await db.Outbox
            .AsNoTracking()
            .CountAsync(e => e.GatewayId == GatewayId && e.SentAtUtc == null, ct)
            .ConfigureAwait(false);

        var lastSequence = await db.OutboxSequences
            .AsNoTracking()
            .Where(s => s.GatewayId == GatewayId)
            .Select(s => s.LastSequence)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var stateCounts = await db.Deliveries
            .AsNoTracking()
            .GroupBy(d => d.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byState = stateCounts.ToDictionary(s => s.State, s => s.Count);
        return new SyncStatusView
        {
            GatewayId = GatewayId,
            Queued = queued,
            LastSequence = lastSequence,
            Deliveries = new DeliverySummary
            {
                Queued = byState.GetValueOrDefault("QUEUED"),
                Syncing = byState.GetValueOrDefault("SYNCING"),
                Delivered = byState.GetValueOrDefault("DELIVERED"),
                Acknowledged = byState.GetValueOrDefault("ACKNOWLEDGED"),
                WaitingForSync = byState.GetValueOrDefault("WAITING_FOR_SYNC"),
                Failed = byState.GetValueOrDefault("FAILED"),
            },
        };
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    private static async Task AddOutboxEntryAsync(EdgeDbContext db, string gatewayId, Message message, CancellationToken ct)
    {
        await OutboxWriter.AddOutboxEntryAsync(db, gatewayId, new SyncItem
        {
            Kind = SyncItemKind.Message,
            Sequence = 0,
            Message = message,
        }, ct).ConfigureAwait(false);
    }

    private static Delivery BuildDelivery(string messageId, Participant recipient, string now) => new()
    {
        Id = IdGenerator.NewId(),
        MessageId = messageId,
        Recipient = recipient,
        State = DeliveryState.Queued,
        Attempts = 0,
        MaxAttempts = 5,
        QueuedAt = now,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static async Task<IReadOnlyList<MessageView>> AttachDeliveriesAsync(EdgeDbContext db, IReadOnlyList<MessageRecord> messages, CancellationToken ct)
    {
        if (messages.Count == 0)
        {
            return Array.Empty<MessageView>();
        }

        var ids = messages.Select(m => m.Id).ToList();
        var deliveries = await db.Deliveries
            .AsNoTracking()
            .Where(d => ids.Contains(d.MessageId))
            .OrderBy(d => d.RecipientAddress)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byMessage = deliveries
            .GroupBy(d => d.MessageId)
            .ToDictionary(g => g.Key, g => g.Select(d => d.Envelope).ToList());

        return messages
            .Select(m => new MessageView
            {
                Message = m.Envelope,
                Deliveries = byMessage.TryGetValue(m.Id, out var dl) ? dl : new List<Delivery>(),
            })
            .ToList();
    }

    private static async Task UpsertParticipantsAsync(EdgeDbContext db, IEnumerable<Participant> participants, CancellationToken ct)
    {
        var distinct = participants
            .Where(p => p is not null)
            .DistinctBy(p => p.Address)
            .ToList();
        if (distinct.Count == 0)
        {
            return;
        }

        var addresses = distinct.Select(p => p.Address).ToHashSet();
        var existing = await db.Participants
            .Where(p => addresses.Contains(p.Address))
            .Select(p => p.Address)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var existingSet = existing.ToHashSet();

        foreach (var participant in distinct)
        {
            if (!existingSet.Contains(participant.Address))
            {
                db.Participants.Add(ParticipantRecord.FromParticipant(participant));
                existingSet.Add(participant.Address);
            }
        }
    }

    private static async Task<Dictionary<string, Participant>> LoadParticipantDirectoryAsync(
        EdgeDbContext db,
        IEnumerable<string> addresses,
        CancellationToken ct)
    {
        var distinct = addresses.Where(a => a is not null).Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<string, Participant>();
        }

        var records = await db.Participants
            .AsNoTracking()
            .Where(p => distinct.Contains(p.Address))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return records.ToDictionary(p => p.Address, p => p.Envelope);
    }

    private static ConversationView ToConversationView(
        Conversation conversation,
        ConversationStats? stat,
        IReadOnlyDictionary<string, Participant> directory)
    {
        var participants = conversation.Participants
            .OrderBy(p => p.ParticipantAddress)
            .Select(p => directory.TryGetValue(p.ParticipantAddress, out var participant)
                ? participant
                : new Participant { Address = p.ParticipantAddress, DisplayName = p.ParticipantAddress })
            .ToList();

        return new ConversationView
        {
            Id = conversation.Id,
            Title = conversation.Title,
            Participants = participants,
            MessageCount = stat?.Count ?? 0,
            LastMessageAt = stat?.Last,
            CreatedAt = conversation.CreatedAt,
        };
    }

    private sealed class ConversationStats
    {
        public string ConversationId { get; set; } = null!;
        public int Count { get; set; }
        public string? Last { get; set; }
    }

    private static string BuildResponseBody(AnswerTaskRequest request)
    {
        if (request.Decision is { } decision)
        {
            var decisionToken = ProtocolJsonConversions.WireToken(decision);
            return string.IsNullOrWhiteSpace(request.Reason) ? decisionToken : $"{decisionToken}: {request.Reason}";
        }

        return request.Text ?? string.Empty;
    }
}
