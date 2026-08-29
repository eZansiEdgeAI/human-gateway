using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HumanGateway.Edge.Storage.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "artifacts",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    hash = table.Column<string>(type: "TEXT", nullable: false),
                    size_bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    mime_type = table.Column<string>(type: "TEXT", nullable: false),
                    json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_artifacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deliveries",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    message_id = table.Column<string>(type: "TEXT", nullable: false),
                    recipient_address = table.Column<string>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliveries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    conversation_id = table.Column<string>(type: "TEXT", nullable: false),
                    sender_address = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    content_hash = table.Column<string>(type: "TEXT", nullable: false),
                    json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "participants",
                columns: table => new
                {
                    address = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: true),
                    display_name = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: true),
                    gateway_id = table.Column<string>(type: "TEXT", nullable: true),
                    json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_participants", x => x.address);
                });

            migrationBuilder.CreateTable(
                name: "conversation_participants",
                columns: table => new
                {
                    conversation_id = table.Column<string>(type: "TEXT", nullable: false),
                    participant_address = table.Column<string>(type: "TEXT", nullable: false),
                    joined_at = table.Column<string>(type: "TEXT", nullable: false)
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
                name: "artifacts");

            migrationBuilder.DropTable(
                name: "conversation_participants");

            migrationBuilder.DropTable(
                name: "deliveries");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "participants");
        }
    }
}
