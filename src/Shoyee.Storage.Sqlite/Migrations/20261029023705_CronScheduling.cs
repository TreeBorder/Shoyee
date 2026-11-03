using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shoyee.Storage.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class CronScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "failure_streak",
                table: "cron_jobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "lane_id",
                table: "cron_jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_error",
                table: "cron_jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_status",
                table: "cron_jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mode",
                table: "cron_jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "Work");

            migrationBuilder.AddColumn<long>(
                name: "next_run_at",
                table: "cron_jobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "session_id",
                table: "cron_jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_cron_jobs_next_run_at",
                table: "cron_jobs",
                column: "next_run_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_cron_jobs_next_run_at",
                table: "cron_jobs");

            migrationBuilder.DropColumn(
                name: "failure_streak",
                table: "cron_jobs");

            migrationBuilder.DropColumn(
                name: "lane_id",
                table: "cron_jobs");

            migrationBuilder.DropColumn(
                name: "last_error",
                table: "cron_jobs");

            migrationBuilder.DropColumn(
                name: "last_status",
                table: "cron_jobs");

            migrationBuilder.DropColumn(
                name: "mode",
                table: "cron_jobs");

            migrationBuilder.DropColumn(
                name: "next_run_at",
                table: "cron_jobs");

            migrationBuilder.DropColumn(
                name: "session_id",
                table: "cron_jobs");
        }
    }
}
