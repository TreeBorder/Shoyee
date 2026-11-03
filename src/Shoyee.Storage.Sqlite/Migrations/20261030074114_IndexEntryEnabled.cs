using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shoyee.Storage.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class IndexEntryEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "enabled",
                table: "index_entries",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "enabled",
                table: "index_entries");
        }
    }
}
