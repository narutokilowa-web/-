using System;
using Mfop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mfop.Access.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OfflineTrust : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "disable_reason",
                schema: "access",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "disabled_at",
                schema: "access",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "disabled_by",
                schema: "access",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "public_key",
                schema: "access",
                table: "devices",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "device_clock_samples",
                schema: "access",
                columns: table => new
                {
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boot_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    server_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    elapsed_ms = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_clock_samples", x => new { x.device_id, x.boot_id });
                });

            migrationBuilder.CreateTable(
                name: "device_nonces",
                schema: "access",
                columns: table => new
                {
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nonce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_nonces", x => new { x.device_id, x.nonce });
                });

            migrationBuilder.CreateTable(
                name: "idp_tasks",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idp_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    done_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    alert = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idp_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "offline_grants",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    roles = table.Column<string[]>(type: "text[]", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    policy_hours = table.Column<int>(type: "integer", nullable: false),
                    key_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    signature = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offline_grants", x => x.id);
                    table.ForeignKey(
                        name: "fk_offline_grants_devices_device_id",
                        column: x => x.device_id,
                        principalSchema: "access",
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "policy_settings",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_policy_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "offline_grant_revocations",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offline_grant_revocations", x => x.id);
                    table.ForeignKey(
                        name: "fk_offline_grant_revocations_offline_grants_grant_id",
                        column: x => x.grant_id,
                        principalSchema: "access",
                        principalTable: "offline_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_idp_tasks_done_at_next_attempt_at",
                schema: "access",
                table: "idp_tasks",
                columns: new[] { "done_at", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_offline_grant_revocations_grant_id",
                schema: "access",
                table: "offline_grant_revocations",
                column: "grant_id");

            migrationBuilder.CreateIndex(
                name: "ix_offline_grants_device_id_expires_at",
                schema: "access",
                table: "offline_grants",
                columns: new[] { "device_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_offline_grants_user_id",
                schema: "access",
                table: "offline_grants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_policy_settings_key",
                schema: "access",
                table: "policy_settings",
                column: "key",
                unique: true);

            migrationBuilder.AppendOnly("access", "offline_grants");
            migrationBuilder.AppendOnly("access", "offline_grant_revocations");
            migrationBuilder.AppendOnly("access", "device_nonces");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_clock_samples",
                schema: "access");

            migrationBuilder.DropTable(
                name: "device_nonces",
                schema: "access");

            migrationBuilder.DropTable(
                name: "idp_tasks",
                schema: "access");

            migrationBuilder.DropTable(
                name: "offline_grant_revocations",
                schema: "access");

            migrationBuilder.DropTable(
                name: "policy_settings",
                schema: "access");

            migrationBuilder.DropTable(
                name: "offline_grants",
                schema: "access");

            migrationBuilder.DropColumn(
                name: "disable_reason",
                schema: "access",
                table: "users");

            migrationBuilder.DropColumn(
                name: "disabled_at",
                schema: "access",
                table: "users");

            migrationBuilder.DropColumn(
                name: "disabled_by",
                schema: "access",
                table: "users");

            migrationBuilder.DropColumn(
                name: "public_key",
                schema: "access",
                table: "devices");
        }
    }
}
