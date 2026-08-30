using HumanGateway.Edge.Storage.Entities;
using HumanGateway.Protocol.Models;
using Microsoft.EntityFrameworkCore;

namespace HumanGateway.Edge.Storage;

/// <summary>
/// The Edge Gateway's durable SQLite store (EDGE-FR-02): conversations, messages, deliveries, artifacts, and
/// participants. Protocol envelopes are stored as canonical wire JSON with denormalised scalar columns for
/// indexed querying. The durability PRAGMAs (WAL + synchronous=NORMAL) are applied by
/// <see cref="SqliteConnectionFactory"/> / the DI interceptor, not here.
/// </summary>
public sealed class EdgeDbContext : DbContext
{
    /// <summary>Creates the context over the supplied options (connection string + PRAGMA interceptor).</summary>
    public EdgeDbContext(DbContextOptions<EdgeDbContext> options)
        : base(options)
    {
    }

    /// <summary>Conversations (local grouping + membership).</summary>
    public DbSet<Conversation> Conversations => Set<Conversation>();

    /// <summary>Conversation membership join rows.</summary>
    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();

    /// <summary>Stored message envelopes.</summary>
    public DbSet<MessageRecord> Messages => Set<MessageRecord>();

    /// <summary>Stored per-recipient delivery records.</summary>
    public DbSet<DeliveryRecord> Deliveries => Set<DeliveryRecord>();

    /// <summary>Stored human task records (local-store concept; tasks travel inside message envelopes).</summary>
    public DbSet<HumanTaskRecord> Tasks => Set<HumanTaskRecord>();

    /// <summary>Stored artifact metadata (bytes live on the filesystem).</summary>
    public DbSet<ArtifactRecord> Artifacts => Set<ArtifactRecord>();

    /// <summary>Local participant directory.</summary>
    public DbSet<ParticipantRecord> Participants => Set<ParticipantRecord>();

    /// <summary>Durable outbox entries (EDGE-FR-04).</summary>
    public DbSet<OutboxEntryRecord> Outbox => Set<OutboxEntryRecord>();

    /// <summary>Durable per-gateway sequence counters (EDGE-FR-04, SYNC-FR-01).</summary>
    public DbSet<OutboxSequence> OutboxSequences => Set<OutboxSequence>();

    /// <summary>Durable inbox entries (SYNC-FR-01).</summary>
    public DbSet<InboxEntryRecord> Inbox => Set<InboxEntryRecord>();

    /// <summary>Durable applied-batch records (SYNC-FR-02, NF-05).</summary>
    public DbSet<IdempotencyRecord> Idempotency => Set<IdempotencyRecord>();

    /// <summary>Durable per-gateway sync-cursor state (SYNC-FR-03, SYNC-FR-02).</summary>
    public DbSet<SyncCursorRecord> SyncCursors => Set<SyncCursorRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Conversation>(conversation =>
        {
            conversation.ToTable("conversations");
            conversation.HasKey(e => e.Id);
            conversation.Property(e => e.Id).HasColumnName("id").IsRequired();
            conversation.Property(e => e.Title).HasColumnName("title");
            conversation.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            conversation.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<ConversationParticipant>(membership =>
        {
            membership.ToTable("conversation_participants");
            membership.HasKey(e => new { e.ConversationId, e.ParticipantAddress });
            membership.Property(e => e.ConversationId).HasColumnName("conversation_id").IsRequired();
            membership.Property(e => e.ParticipantAddress).HasColumnName("participant_address").IsRequired();
            membership.Property(e => e.JoinedAt).HasColumnName("joined_at").IsRequired();

            membership
                .HasOne(e => e.Conversation)
                .WithMany(c => c.Participants)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            membership
                .HasOne(e => e.Participant)
                .WithMany()
                .HasForeignKey(e => e.ParticipantAddress)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MessageRecord>(message =>
        {
            message.ToTable("messages");
            message.HasKey(e => e.Id);
            message.Property(e => e.Id).HasColumnName("id").IsRequired();
            message.Property(e => e.ConversationId).HasColumnName("conversation_id").IsRequired();
            message.Property(e => e.SenderAddress).HasColumnName("sender_address").IsRequired();
            message.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            message.Property(e => e.ContentHash).HasColumnName("content_hash").IsRequired();
            message.Property(e => e.Envelope)
                .HasColumnName("json")
                .IsRequired()
                .HasConversion(ProtocolJsonConversions.CanonicalJson<Message>());

            message.HasIndex(e => e.ConversationId).HasDatabaseName("ix_messages_conversation_id");
            message.HasIndex(e => new { e.ConversationId, e.CreatedAt }).HasDatabaseName("ix_messages_conversation_created");
            message.HasIndex(e => e.SenderAddress).HasDatabaseName("ix_messages_sender_address");
        });

        modelBuilder.Entity<DeliveryRecord>(delivery =>
        {
            delivery.ToTable("deliveries");
            delivery.HasKey(e => e.Id);
            delivery.Property(e => e.Id).HasColumnName("id").IsRequired();
            delivery.Property(e => e.MessageId).HasColumnName("message_id").IsRequired();
            delivery.Property(e => e.RecipientAddress).HasColumnName("recipient_address").IsRequired();
            delivery.Property(e => e.State).HasColumnName("state").IsRequired();
            delivery.Property(e => e.Envelope)
                .HasColumnName("json")
                .IsRequired()
                .HasConversion(ProtocolJsonConversions.CanonicalJson<Delivery>());

            delivery.HasIndex(e => e.MessageId).HasDatabaseName("ix_deliveries_message_id");
            delivery.HasIndex(e => e.RecipientAddress).HasDatabaseName("ix_deliveries_recipient_address");
            delivery.HasIndex(e => e.State).HasDatabaseName("ix_deliveries_state");

            // One delivery record per (message, recipient) — enforced by a unique index (PROTO-FR-05).
            delivery.HasIndex(e => new { e.MessageId, e.RecipientAddress })
                .IsUnique()
                .HasDatabaseName("ux_deliveries_message_recipient");
        });

        modelBuilder.Entity<HumanTaskRecord>(task =>
        {
            task.ToTable("tasks");
            task.HasKey(e => e.Id);
            task.Property(e => e.Id).HasColumnName("id").IsRequired();
            task.Property(e => e.Status).HasColumnName("status").IsRequired();
            task.Property(e => e.Kind).HasColumnName("kind");
            task.Property(e => e.WorkflowRef).HasColumnName("workflow_ref").IsRequired();
            task.Property(e => e.RequestMessageId).HasColumnName("request_message_id").IsRequired();
            task.Property(e => e.ResponseMessageId).HasColumnName("response_message_id");
            task.Property(e => e.Envelope)
                .HasColumnName("json")
                .IsRequired()
                .HasConversion(ProtocolJsonConversions.CanonicalJson<HumanTask>());

            task.HasIndex(e => e.Status).HasDatabaseName("ix_tasks_status");
            task.HasIndex(e => e.WorkflowRef).HasDatabaseName("ix_tasks_workflow_ref");
            task.HasIndex(e => e.RequestMessageId).HasDatabaseName("ix_tasks_request_message_id");
        });

        modelBuilder.Entity<ArtifactRecord>(artifact =>
        {
            artifact.ToTable("artifacts");
            artifact.HasKey(e => e.Id);
            artifact.Property(e => e.Id).HasColumnName("id").IsRequired();
            artifact.Property(e => e.Hash).HasColumnName("hash").IsRequired();
            artifact.Property(e => e.SizeBytes).HasColumnName("size_bytes").IsRequired();
            artifact.Property(e => e.MimeType).HasColumnName("mime_type").IsRequired();
            artifact.Property(e => e.Envelope)
                .HasColumnName("json")
                .IsRequired()
                .HasConversion(ProtocolJsonConversions.CanonicalJson<Artifact>());

            // Non-unique: multiple artifact IDs may reference the same bytes (dedup, ARTF-FR-01).
            artifact.HasIndex(e => e.Hash).HasDatabaseName("ix_artifacts_hash");
        });

        modelBuilder.Entity<ParticipantRecord>(participant =>
        {
            participant.ToTable("participants");
            participant.HasKey(e => e.Address);
            participant.Property(e => e.Address).HasColumnName("address").IsRequired();
            participant.Property(e => e.Kind).HasColumnName("kind");
            participant.Property(e => e.DisplayName).HasColumnName("display_name").IsRequired();
            participant.Property(e => e.UserId).HasColumnName("user_id");
            participant.Property(e => e.GatewayId).HasColumnName("gateway_id");
            participant.Property(e => e.Envelope)
                .HasColumnName("json")
                .IsRequired()
                .HasConversion(ProtocolJsonConversions.CanonicalJson<Participant>());
        });

        modelBuilder.Entity<OutboxEntryRecord>(outbox =>
        {
            outbox.ToTable("outbox");
            outbox.HasKey(e => e.Id);
            outbox.Property(e => e.Id).HasColumnName("id").IsRequired();
            outbox.Property(e => e.GatewayId).HasColumnName("gateway_id").IsRequired();
            outbox.Property(e => e.Sequence).HasColumnName("sequence").IsRequired();
            outbox.Property(e => e.Item)
                .HasColumnName("json")
                .IsRequired()
                .HasConversion(ProtocolJsonConversions.CanonicalJson<SyncItem>());
            outbox.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            outbox.Property(e => e.Attempts).HasColumnName("attempts").IsRequired();
            outbox.Property(e => e.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
            outbox.Property(e => e.SentAtUtc).HasColumnName("sent_at_utc");

            // The unique per-gateway sequence index is both the ordering accelerator for the pending scan
            // (gateway + sequence ascending) and the safety net against a duplicate sequence (EDGE-FR-04).
            outbox.HasIndex(e => new { e.GatewayId, e.Sequence })
                .IsUnique()
                .HasDatabaseName("ux_outbox_gateway_sequence");
        });

        modelBuilder.Entity<OutboxSequence>(sequence =>
        {
            sequence.ToTable("outbox_sequences");
            sequence.HasKey(e => e.GatewayId);
            sequence.Property(e => e.GatewayId).HasColumnName("gateway_id").IsRequired();
            sequence.Property(e => e.LastSequence).HasColumnName("last_sequence").IsRequired();
        });

        modelBuilder.Entity<InboxEntryRecord>(inbox =>
        {
            inbox.ToTable("inbox");
            inbox.HasKey(e => e.Id);
            inbox.Property(e => e.Id).HasColumnName("id").IsRequired();
            inbox.Property(e => e.GatewayId).HasColumnName("gateway_id").IsRequired();
            inbox.Property(e => e.Sequence).HasColumnName("sequence").IsRequired();
            inbox.Property(e => e.MessageId).HasColumnName("message_id");
            inbox.Property(e => e.Item)
                .HasColumnName("json")
                .IsRequired()
                .HasConversion(ProtocolJsonConversions.CanonicalJson<SyncItem>());
            inbox.Property(e => e.ReceivedAtUtc).HasColumnName("received_at_utc").IsRequired();

            // One inbox row per message (dedup, SYNC-FR-02). SQLite allows multiple NULLs in a unique index,
            // so non-message entries (delivery/artifact/ack) are unaffected.
            inbox.HasIndex(e => e.MessageId)
                .IsUnique()
                .HasDatabaseName("ux_inbox_message_id");

            inbox.HasIndex(e => new { e.GatewayId, e.Sequence })
                .HasDatabaseName("ix_inbox_gateway_sequence");
        });

        modelBuilder.Entity<IdempotencyRecord>(idempotency =>
        {
            idempotency.ToTable("idempotency");
            idempotency.HasKey(e => new { e.BatchId, e.IdempotencyKey });
            idempotency.Property(e => e.BatchId).HasColumnName("batch_id").IsRequired();
            idempotency.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").IsRequired();
            idempotency.Property(e => e.AppliedAtUtc).HasColumnName("applied_at_utc").IsRequired();
        });

        modelBuilder.Entity<SyncCursorRecord>(cursor =>
        {
            cursor.ToTable("sync_cursors");
            cursor.HasKey(e => e.GatewayId);
            cursor.Property(e => e.GatewayId).HasColumnName("gateway_id").IsRequired();
            cursor.Property(e => e.PushCursor).HasColumnName("push_cursor");
            cursor.Property(e => e.PullCursor).HasColumnName("pull_cursor");
            cursor.Property(e => e.InFlightBatchId).HasColumnName("in_flight_batch_id");
            cursor.Property(e => e.InFlightIdempotencyKey).HasColumnName("in_flight_idempotency_key");
            cursor.Property(e => e.InFlightAfterSequence).HasColumnName("in_flight_after_sequence");
        });
    }
}
