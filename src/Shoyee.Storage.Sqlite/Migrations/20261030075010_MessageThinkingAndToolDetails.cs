using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shoyee.Storage.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class MessageThinkingAndToolDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "thinking",
                table: "messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tool_arguments",
                table: "messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tool_call_id",
                table: "messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tool_result",
                table: "messages",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "thinking",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "tool_arguments",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "tool_call_id",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "tool_result",
                table: "messages");
        }
    }
}
