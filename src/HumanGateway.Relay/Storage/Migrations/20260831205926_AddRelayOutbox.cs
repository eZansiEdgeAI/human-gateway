using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HumanGateway.Relay.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddRelayOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "relay_outbox",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    gateway_id = table.Column<string>(type: "text", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    message_id = table.Column<string>(type: "text", nullable: true),
                    json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relay_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "relay_outbox_sequences",
                columns: table => new
                {
                    gateway_id = table.Column<string>(type: "text", nullable: false),
                    last_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relay_outbox_sequences", x => x.gateway_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_relay_outbox_gateway_sequence",
                table: "relay_outbox",
                columns: new[] { "gateway_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ux_relay_outbox_gateway_message",
                table: "relay_outbox",
                columns: new[] { "gateway_id", "message_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "relay_outbox");

            migrationBuilder.DropTable(
                name: "relay_outbox_sequences");
        }
    }
}
