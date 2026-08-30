using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HumanGateway.Edge.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncCursors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_cursors",
                columns: table => new
                {
                    gateway_id = table.Column<string>(type: "TEXT", nullable: false),
                    push_cursor = table.Column<string>(type: "TEXT", nullable: true),
                    pull_cursor = table.Column<string>(type: "TEXT", nullable: true),
                    in_flight_batch_id = table.Column<string>(type: "TEXT", nullable: true),
                    in_flight_idempotency_key = table.Column<string>(type: "TEXT", nullable: true),
                    in_flight_after_sequence = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_cursors", x => x.gateway_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_cursors");
        }
    }
}
