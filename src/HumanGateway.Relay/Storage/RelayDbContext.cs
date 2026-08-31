using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace HumanGateway.Relay.Storage;

/// <summary>
/// The Cloud Relay's durable PostgreSQL store (RELAY-FR-01): gateways, conversations, messages, deliveries,
/// artifacts (metadata + BYTEA blobs), participants, and the sync model (inbox, idempotency, cursors).
/// Protocol envelopes are stored as canonical wire JSON in jsonb columns with denormalised scalar columns for
/// indexed querying. Column names follow snake_case (PostgreSQL convention); the design-time factory in
/// <see cref="RelayDbContextFactory"/> keeps the EF tooling able to build the model without a live database.
/// </summary>
public sealed class RelayDbContext : DbContext
{
    /// <summary>Creates the context over the supplied options (connection string).</summary>
    public RelayDbContext(DbContextOptions<RelayDbContext> options)
        : base(options)
    {
    }

    /// <summary>Registered Edge Gateways (gateway.schema.json, RELAY-FR-03).</summary>
    public DbSet<GatewayRecord> Gateways => Set<GatewayRecord>();

    /// <summary>Conversations (shared grouping for cross-school exchange).</summary>
    public DbSet<Conversation> Conversations => Set<Conversation>();

    /// <summary>Conversation membership join rows.</summary>
    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();

    /// <summary>Stored message envelopes from all registered gateways.</summary>
    public DbSet<MessageRecord> Messages => Set<MessageRecord>();

    /// <summary>Stored per-recipient delivery records.</summary>
    public DbSet<DeliveryRecord> Deliveries => Set<DeliveryRecord>();

    /// <summary>Stored artifact metadata (bytes live in <see cref="ArtifactBlobs"/>).</summary>
    public DbSet<ArtifactRecord> Artifacts => Set<ArtifactRecord>();

    /// <summary>Content-addressed artifact bytes (RELAY-FR-01, BYTEA).</summary>
    public DbSet<ArtifactBlobRecord> ArtifactBlobs => Set<ArtifactBlobRecord>();

    /// <summary>Participant directory.</summary>
    public DbSet<ParticipantRecord> Participants => Set<ParticipantRecord>();

    /// <summary>Durable per-gateway sync-cursor state (SYNC-FR-03).</summary>
    public DbSet<SyncCursorRecord> SyncCursors => Set<SyncCursorRecord>();

    /// <summary>Durable inbox entries (applied PUSH items, SYNC-FR-01).</summary>
    public DbSet<InboxEntryRecord> Inbox => Set<InboxEntryRecord>();

    /// <summary>Durable applied-batch records (SYNC-FR-02, NF-05).</summary>
    public DbSet<IdempotencyRecord> Idempotency => Set<IdempotencyRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<GatewayRecord>(gateway =>
        {
            gateway.ToTable("gateways");
            gateway.HasKey(e => e.GatewayId);
            gateway.Property(e => e.GatewayId).HasColumnName("gateway_id").IsRequired();
            gateway.Property(e => e.DisplayName).HasColumnName("display_name");
            gateway.Property(e => e.Status).HasColumnName("status");
            gateway.Property(e => e.RegistrationTokenFingerprint).HasColumnName("registration_token_fingerprint");
            gateway.Property(e => e.TokenIssuedAt).HasColumnName("token_issued_at");
            gateway.Property(e => e.TokenExpiresAt).HasColumnName("token_expires_at");
            gateway.Property(e => e.RegisteredAt).HasColumnName("registered_at");
            gateway.Property(e => e.SuspendedAt).HasColumnName("suspended_at");
            gateway.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            gateway.Property(e => e.LastSeenAt).HasColumnName("last_seen_at");
            gateway.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            gateway.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            // A token fingerprint is held by exactly one gateway (SP-07).
            gateway.HasIndex(e => e.RegistrationTokenFingerprint)
                .IsUnique()
                .HasDatabaseName("ux_gateways_token_fingerprint");
            gateway.HasIndex(e => e.Status).HasDatabaseName("ix_gateways_status");
            gateway.HasIndex(e => e.LastSeenAt).HasDatabaseName("ix_gateways_last_seen_at");
        });

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
                .HasColumnType("jsonb")
                .IsRequired()
                .HasConversion(RelayJsonConversions.CanonicalJson<Message>());

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
                .HasColumnType("jsonb")
                .IsRequired()
                .HasConversion(RelayJsonConversions.CanonicalJson<Delivery>());

            delivery.HasIndex(e => e.MessageId).HasDatabaseName("ix_deliveries_message_id");
            delivery.HasIndex(e => e.RecipientAddress).HasDatabaseName("ix_deliveries_recipient_address");
            delivery.HasIndex(e => e.State).HasDatabaseName("ix_deliveries_state");

            // One delivery record per (message, recipient) — enforced by a unique index (PROTO-FR-05).
            delivery.HasIndex(e => new { e.MessageId, e.RecipientAddress })
                .IsUnique()
                .HasDatabaseName("ux_deliveries_message_recipient");
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
                .HasColumnType("jsonb")
                .IsRequired()
                .HasConversion(RelayJsonConversions.CanonicalJson<Artifact>());

            // Non-unique: multiple artifact IDs may reference the same bytes (dedup, ARTF-FR-01).
            artifact.HasIndex(e => e.Hash).HasDatabaseName("ix_artifacts_hash");
        });

        modelBuilder.Entity<ArtifactBlobRecord>(blob =>
        {
            blob.ToTable("artifact_blobs");
            blob.HasKey(e => e.Hash);
            blob.Property(e => e.Hash).HasColumnName("hash").IsRequired();
            blob.Property(e => e.Data).HasColumnName("data").HasColumnType("bytea").IsRequired();
            blob.Property(e => e.SizeBytes).HasColumnName("size_bytes").IsRequired();
            blob.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
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
                .HasColumnType("jsonb")
                .IsRequired()
                .HasConversion(RelayJsonConversions.CanonicalJson<Participant>());
        });

        modelBuilder.Entity<SyncCursorRecord>(cursor =>
        {
            cursor.ToTable("sync_cursors");
            cursor.HasKey(e => e.GatewayId);
            cursor.Property(e => e.GatewayId).HasColumnName("gateway_id").IsRequired();
            cursor.Property(e => e.PushCursor).HasColumnName("push_cursor");
            cursor.Property(e => e.PullCursor).HasColumnName("pull_cursor");
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
                .HasColumnType("jsonb")
                .IsRequired()
                .HasConversion(RelayJsonConversions.CanonicalJson<SyncItem>());
            inbox.Property(e => e.ReceivedAtUtc).HasColumnName("received_at_utc").IsRequired();

            // One inbox row per message (dedup, SYNC-FR-02). PostgreSQL allows multiple NULLs in a unique
            // index, so non-message entries (delivery/artifact/ack) are unaffected.
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
    }
}
