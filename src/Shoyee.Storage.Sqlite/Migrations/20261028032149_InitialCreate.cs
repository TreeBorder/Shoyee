using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shoyee.Storage.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "approvals",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    request_id = table.Column<string>(type: "TEXT", nullable: false),
                    session_id = table.Column<string>(type: "TEXT", nullable: false),
                    turn_id = table.Column<string>(type: "TEXT", nullable: false),
                    tool_name = table.Column<string>(type: "TEXT", nullable: false),
                    arguments = table.Column<string>(type: "TEXT", nullable: false),
                    decision = table.Column<string>(type: "TEXT", nullable: true),
                    decided_by = table.Column<string>(type: "TEXT", nullable: true),
                    decided_at = table.Column<long>(type: "INTEGER", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approvals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cron_executions",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    job_id = table.Column<string>(type: "TEXT", nullable: false),
                    scheduled_for = table.Column<long>(type: "INTEGER", nullable: false),
                    started_at = table.Column<long>(type: "INTEGER", nullable: true),
                    finished_at = table.Column<long>(type: "INTEGER", nullable: true),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    result = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cron_executions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cron_jobs",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    schedule = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false),
                    deliver = table.Column<string>(type: "TEXT", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    last_run_at = table.Column<long>(type: "INTEGER", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cron_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deliveries",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    lane_id = table.Column<string>(type: "TEXT", nullable: false),
                    session_id = table.Column<string>(type: "TEXT", nullable: true),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    next_attempt_at = table.Column<long>(type: "INTEGER", nullable: true),
                    last_error = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliveries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "index_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false),
                    scope = table.Column<string>(type: "TEXT", nullable: false),
                    tags = table.Column<string>(type: "TEXT", nullable: true),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    pinned = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_index_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lanes",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    channel = table.Column<string>(type: "TEXT", nullable: false),
                    mode = table.Column<string>(type: "TEXT", nullable: false),
                    external_key = table.Column<string>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", nullable: true),
                    active_session_id = table.Column<string>(type: "TEXT", nullable: true),
                    last_inbound_at = table.Column<long>(type: "INTEGER", nullable: true),
                    last_interaction_at = table.Column<long>(type: "INTEGER", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lanes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    lane_id = table.Column<string>(type: "TEXT", nullable: false),
                    session_id = table.Column<string>(type: "TEXT", nullable: true),
                    round_no = table.Column<int>(type: "INTEGER", nullable: true),
                    role = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    tool_name = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rounds",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    lane_id = table.Column<string>(type: "TEXT", nullable: false),
                    round_no = table.Column<int>(type: "INTEGER", nullable: false),
                    request_json = table.Column<string>(type: "TEXT", nullable: false),
                    response_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rounds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "send_attempts",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    lane_id = table.Column<string>(type: "TEXT", nullable: false),
                    since_last_inbound_seconds = table.Column<long>(type: "INTEGER", nullable: false),
                    result = table.Column<string>(type: "TEXT", nullable: false),
                    error = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_send_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "session_state",
                columns: table => new
                {
                    storage_key = table.Column<string>(type: "TEXT", nullable: false),
                    state_json = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_state", x => x.storage_key);
                });

            migrationBuilder.CreateTable(
                name: "states",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    agent_editable = table.Column<bool>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_states", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "summaries",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    scope = table.Column<string>(type: "TEXT", nullable: false),
                    target_id = table.Column<string>(type: "TEXT", nullable: false),
                    through_round = table.Column<int>(type: "INTEGER", nullable: false),
                    round_count = table.Column<int>(type: "INTEGER", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_summaries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usage",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    session_id = table.Column<string>(type: "TEXT", nullable: true),
                    turn_id = table.Column<string>(type: "TEXT", nullable: true),
                    purpose = table.Column<string>(type: "TEXT", nullable: false),
                    model = table.Column<string>(type: "TEXT", nullable: false),
                    input_tokens = table.Column<int>(type: "INTEGER", nullable: false),
                    output_tokens = table.Column<int>(type: "INTEGER", nullable: false),
                    cache_hit_tokens = table.Column<int>(type: "INTEGER", nullable: false),
                    cache_miss_tokens = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    mode = table.Column<string>(type: "TEXT", nullable: false),
                    origin = table.Column<string>(type: "TEXT", nullable: false),
                    lane_id = table.Column<string>(type: "TEXT", nullable: true),
                    session_key = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: true),
                    parent_session_id = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    last_active_at = table.Column<long>(type: "INTEGER", nullable: false),
                    archived = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_sessions_lanes_lane_id",
                        column: x => x.lane_id,
                        principalTable: "lanes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_approvals_request_id",
                table: "approvals",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_approvals_session_id_created_at",
                table: "approvals",
                columns: new[] { "session_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_cron_executions_job_id_scheduled_for",
                table: "cron_executions",
                columns: new[] { "job_id", "scheduled_for" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_state_updated_at",
                table: "deliveries",
                columns: new[] { "state", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_index_entries_key",
                table: "index_entries",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lanes_channel_external_key_mode",
                table: "lanes",
                columns: new[] { "channel", "external_key", "mode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_created_at",
                table: "messages",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_messages_lane_id_id",
                table: "messages",
                columns: new[] { "lane_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_lane_id_round_no",
                table: "messages",
                columns: new[] { "lane_id", "round_no" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_session_id_id",
                table: "messages",
                columns: new[] { "session_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_rounds_lane_id_round_no",
                table: "rounds",
                columns: new[] { "lane_id", "round_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_send_attempts_lane_id_created_at",
                table: "send_attempts",
                columns: new[] { "lane_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sessions_lane_id",
                table: "sessions",
                column: "lane_id");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_last_active_at",
                table: "sessions",
                column: "last_active_at");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_session_key_created_at",
                table: "sessions",
                columns: new[] { "session_key", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_summaries_scope_target_id_created_at",
                table: "summaries",
                columns: new[] { "scope", "target_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_usage_created_at",
                table: "usage",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approvals");

            migrationBuilder.DropTable(
                name: "cron_executions");

            migrationBuilder.DropTable(
                name: "cron_jobs");

            migrationBuilder.DropTable(
                name: "deliveries");

            migrationBuilder.DropTable(
                name: "index_entries");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "rounds");

            migrationBuilder.DropTable(
                name: "send_attempts");

            migrationBuilder.DropTable(
                name: "session_state");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "states");

            migrationBuilder.DropTable(
                name: "summaries");

            migrationBuilder.DropTable(
                name: "usage");

            migrationBuilder.DropTable(
                name: "lanes");
        }
    }
}
