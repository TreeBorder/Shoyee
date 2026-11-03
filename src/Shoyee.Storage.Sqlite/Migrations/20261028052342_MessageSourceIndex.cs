using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shoyee.Storage.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class MessageSourceIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "source_index",
                table: "messages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_session_id_source_index",
                table: "messages",
                columns: new[] { "session_id", "source_index" },
                unique: true,
                filter: "session_id IS NOT NULL AND source_index IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_messages_session_id_source_index",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "source_index",
                table: "messages");
        }
    }
}
