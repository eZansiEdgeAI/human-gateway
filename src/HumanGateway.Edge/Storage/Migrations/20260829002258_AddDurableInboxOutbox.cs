using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HumanGateway.Edge.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableInboxOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency",
                columns: table => new
                {
                    batch_id = table.Column<string>(type: "TEXT", nullable: false),
                    idempotency_key = table.Column<string>(type: "TEXT", nullable: false),
                    applied_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency", x => new { x.batch_id, x.idempotency_key });
                });

            migrationBuilder.CreateTable(
                name: "inbox",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    gateway_id = table.Column<string>(type: "TEXT", nullable: false),
                    sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    message_id = table.Column<string>(type: "TEXT", nullable: true),
                    json = table.Column<string>(type: "TEXT", nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    gateway_id = table.Column<string>(type: "TEXT", nullable: false),
                    sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    sent_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_sequences",
                columns: table => new
                {
                    gateway_id = table.Column<string>(type: "TEXT", nullable: false),
                    last_sequence = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_sequences", x => x.gateway_id);
                });

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
                name: "ux_outbox_gateway_sequence",
                table: "outbox",
                columns: new[] { "gateway_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency");

            migrationBuilder.DropTable(
                name: "inbox");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "outbox_sequences");
        }
    }
}
