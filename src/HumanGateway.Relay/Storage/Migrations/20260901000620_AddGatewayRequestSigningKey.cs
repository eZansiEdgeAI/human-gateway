using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HumanGateway.Relay.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddGatewayRequestSigningKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "request_signing_key",
                table: "gateways",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "request_signing_key",
                table: "gateways");
        }
    }
}
