using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mfop.Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "audit",
                columns: table => new
                {
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_roles = table.Column<string[]>(type: "text[]", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_version = table.Column<int>(type: "integer", nullable: true),
                    changes = table.Column<string>(type: "jsonb", nullable: false),
                    facts = table.Column<string>(type: "jsonb", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: true),
                    payload_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    prev_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.seq);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_actor_user_id",
                schema: "audit",
                table: "audit_events",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_entity_type_entity_id",
                schema: "audit",
                table: "audit_events",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_event_id",
                schema: "audit",
                table: "audit_events",
                column: "event_id",
                unique: true);

            // Append-only guard (ADR-0007): rejects UPDATE/DELETE/TRUNCATE even for roles that have the privilege.
            // This protects against application bugs and accidents. It does NOT stop a privileged DBA
            // (who can disable triggers); tampering is detected by the hash chain and tools/audit-verify.
            migrationBuilder.Sql("""
                CREATE FUNCTION audit.reject_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'audit.% is append-only: % rejected', TG_TABLE_NAME, TG_OP;
                END $$;
                CREATE TRIGGER audit_events_no_update_delete BEFORE UPDATE OR DELETE ON audit.audit_events
                  FOR EACH ROW EXECUTE FUNCTION audit.reject_mutation();
                CREATE TRIGGER audit_events_no_truncate BEFORE TRUNCATE ON audit.audit_events
                  FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "audit");
        }
    }
}
