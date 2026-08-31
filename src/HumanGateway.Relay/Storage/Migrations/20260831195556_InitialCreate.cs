using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HumanGateway.Relay.Storage.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "artifact_blobs",
                columns: table => new
                {
                    hash = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_artifact_blobs", x => x.hash);
                });

            migrationBuilder.CreateTable(
                name: "artifacts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    hash = table.Column<string>(type: "text", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    mime_type = table.Column<string>(type: "text", nullable: false),
                    json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_artifacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deliveries",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    message_id = table.Column<string>(type: "text", nullable: false),
                    recipient_address = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliveries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gateways",
                columns: table => new
                {
                    gateway_id = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    registration_token_fingerprint = table.Column<string>(type: "text", nullable: true),
                    token_issued_at = table.Column<string>(type: "text", nullable: true),
                    token_expires_at = table.Column<string>(type: "text", nullable: true),
                    registered_at = table.Column<string>(type: "text", nullable: true),
                    suspended_at = table.Column<string>(type: "text", nullable: true),
                    revoked_at = table.Column<string>(type: "text", nullable: true),
                    last_seen_at = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gateways", x => x.gateway_id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency",
                columns: table => new
                {
                    batch_id = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    applied_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency", x => new { x.batch_id, x.idempotency_key });
                });

            migrationBuilder.CreateTable(
                name: "inbox",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    gateway_id = table.Column<string>(type: "text", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    message_id = table.Column<string>(type: "text", nullable: true),
                    json = table.Column<string>(type: "jsonb", nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    conversation_id = table.Column<string>(type: "text", nullable: false),
                    sender_address = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "participants",
                columns: table => new
                {
                    address = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    gateway_id = table.Column<string>(type: "text", nullable: true),
                    json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_participants", x => x.address);
                });

            migrationBuilder.CreateTable(
                name: "sync_cursors",
                columns: table => new
                {
                    gateway_id = table.Column<string>(type: "text", nullable: false),
                    push_cursor = table.Column<string>(type: "text", nullable: true),
                    pull_cursor = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_cursors", x => x.gateway_id);
                });

            migrationBuilder.CreateTable(
                name: "conversation_participants",
                columns: table => new
                {
                    conversation_id = table.Column<string>(type: "text", nullable: false),
                    participant_address = table.Column<string>(type: "text", nullable: false),
                    joined_at = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_participants", x => new { x.conversation_id, x.participant_address });
                    table.ForeignKey(
                        name: "FK_conversation_participants_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_conversation_participants_participants_participant_address",
                        column: x => x.participant_address,
                        principalTable: "participants",
                        principalColumn: "address",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_artifacts_hash",
                table: "artifacts",
                column: "hash");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_participants_participant_address",
                table: "conversation_participants",
                column: "participant_address");

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_message_id",
                table: "deliveries",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_recipient_address",
                table: "deliveries",
                column: "recipient_address");

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_state",
                table: "deliveries",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ux_deliveries_message_recipient",
                table: "deliveries",
                columns: new[] { "message_id", "recipient_address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gateways_last_seen_at",
                table: "gateways",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "ix_gateways_status",
                table: "gateways",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_gateways_token_fingerprint",
                table: "gateways",
                column: "registration_token_fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbox_gateway_sequence",
                table: "inbox",
                columns: new[] { "gateway_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ux_inbox_message_id",
                table: "inbox",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_created",
                table: "messages",
                columns: new[] { "conversation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id",
                table: "messages",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_sender_address",
                table: "messages",
                column: "sender_address");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "artifact_blobs");

            migrationBuilder.DropTable(
                name: "artifacts");

            migrationBuilder.DropTable(
                name: "conversation_participants");

            migrationBuilder.DropTable(
                name: "deliveries");

            migrationBuilder.DropTable(
                name: "gateways");

            migrationBuilder.DropTable(
                name: "idempotency");

            migrationBuilder.DropTable(
                name: "inbox");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "sync_cursors");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "participants");
        }
    }
}
