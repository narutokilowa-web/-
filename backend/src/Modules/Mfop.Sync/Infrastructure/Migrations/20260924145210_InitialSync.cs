using System;
using Mfop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mfop.Sync.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sync");

            migrationBuilder.CreateTable(
                name: "device_sync_states",
                schema: "sync",
                columns: table => new
                {
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_pull_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_push_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_reported_pending_commands = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_sync_states", x => x.device_id);
                });

            migrationBuilder.CreateTable(
                name: "sync_commands",
                schema: "sync",
                columns: table => new
                {
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    payload_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    device_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    result_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    result_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_commands", x => x.command_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sync_commands_device_id_received_at",
                schema: "sync",
                table: "sync_commands",
                columns: new[] { "device_id", "received_at" });

            // Receipts are append-only: a command id is recorded once, with its final result.
            migrationBuilder.AppendOnly("sync", "sync_commands");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_sync_states",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "sync_commands",
                schema: "sync");
        }
    }
}
