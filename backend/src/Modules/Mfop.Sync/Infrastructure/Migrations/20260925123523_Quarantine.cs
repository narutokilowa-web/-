using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mfop.Sync.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Quarantine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "estimated_time",
                schema: "sync",
                table: "sync_commands",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "grant_id",
                schema: "sync",
                table: "sync_commands",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "time_confidence",
                schema: "sync",
                table: "sync_commands",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "quarantine",
                schema: "sync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_roles = table.Column<string[]>(type: "text[]", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    device_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    estimated_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    time_confidence = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    grant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    base_version = table.Column<long>(type: "bigint", nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    result_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quarantine", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_quarantine_command_id",
                schema: "sync",
                table: "quarantine",
                column: "command_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quarantine_state",
                schema: "sync",
                table: "quarantine",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quarantine",
                schema: "sync");

            migrationBuilder.DropColumn(
                name: "estimated_time",
                schema: "sync",
                table: "sync_commands");

            migrationBuilder.DropColumn(
                name: "grant_id",
                schema: "sync",
                table: "sync_commands");

            migrationBuilder.DropColumn(
                name: "time_confidence",
                schema: "sync",
                table: "sync_commands");
        }
    }
}
