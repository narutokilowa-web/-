using System;
using Mfop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mfop.Faults.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialFaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "faults");

            migrationBuilder.CreateTable(
                name: "faults",
                schema: "faults",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reported_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    power_at_inspection = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    power_info_source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    power_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    responsible_party = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    responsible_entity_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    capture_channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dismiss_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    dismiss_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    dismissed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    dismissed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmation_count = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_faults", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fault_confirmations",
                schema: "faults",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fault_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    power_at_inspection = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    power_info_source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    power_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    capture_channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fault_confirmations", x => x.id);
                    table.ForeignKey(
                        name: "fk_fault_confirmations_faults_fault_id",
                        column: x => x.fault_id,
                        principalSchema: "faults",
                        principalTable: "faults",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fault_confirmations_fault_id",
                schema: "faults",
                table: "fault_confirmations",
                column: "fault_id");

            migrationBuilder.CreateIndex(
                name: "ix_faults_asset_id_category_state",
                schema: "faults",
                table: "faults",
                columns: new[] { "asset_id", "category", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_faults_state",
                schema: "faults",
                table: "faults",
                column: "state");

            migrationBuilder.AppendOnly("faults", "fault_confirmations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fault_confirmations",
                schema: "faults");

            migrationBuilder.DropTable(
                name: "faults",
                schema: "faults");
        }
    }
}
