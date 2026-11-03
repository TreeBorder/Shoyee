using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shoyee.Storage.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class MessageAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "attachments_json",
                table: "messages",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attachments_json",
                table: "messages");
        }
    }
}
