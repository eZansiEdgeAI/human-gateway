using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HumanGateway.Edge.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddHumanTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: true),
                    workflow_ref = table.Column<string>(type: "TEXT", nullable: false),
                    request_message_id = table.Column<string>(type: "TEXT", nullable: false),
                    response_message_id = table.Column<string>(type: "TEXT", nullable: true),
                    json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tasks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_request_message_id",
                table: "tasks",
                column: "request_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_status",
                table: "tasks",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_workflow_ref",
                table: "tasks",
                column: "workflow_ref");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tasks");
        }
    }
}
