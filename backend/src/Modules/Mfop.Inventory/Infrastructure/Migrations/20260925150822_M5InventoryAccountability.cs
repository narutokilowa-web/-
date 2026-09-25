using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mfop.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class M5InventoryAccountability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "core_return_code_seq",
                schema: "inventory");

            migrationBuilder.CreateSequence(
                name: "stock_issue_code_seq",
                schema: "inventory");

            migrationBuilder.CreateSequence(
                name: "stock_return_code_seq",
                schema: "inventory");

            migrationBuilder.AddColumn<Guid>(
                name: "document_id",
                schema: "inventory",
                table: "stock_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "core_returns",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consume_movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    installed_serial = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    expected_serial = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    declared_by = table.Column<Guid>(type: "uuid", nullable: true),
                    declared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    declared_serial = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    declared_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_by = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_serial = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    serial_mismatch = table.Column<bool>(type: "boolean", nullable: false),
                    receive_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    not_presented_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    waived_by = table.Column<Guid>(type: "uuid", nullable: true),
                    waived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    waive_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    overdue_flagged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_core_returns", x => x.id);
                    table.CheckConstraint("ck_core_returns_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_core_returns_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_core_returns_stock_movements_consume_movement_id",
                        column: x => x.consume_movement_id,
                        principalSchema: "inventory",
                        principalTable: "stock_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "serial_units",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    installed_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    installed_by_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    removed_from_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    historical = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_serial_units", x => x.id);
                    table.ForeignKey(
                        name: "fk_serial_units_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_issues",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reference_work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dispute_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    dispute_evidence_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    unacknowledged_flagged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_issues", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_returns",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    declared_by = table.Column<Guid>(type: "uuid", nullable: false),
                    declared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_returns", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_document_id",
                schema: "inventory",
                table: "stock_movements",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_core_returns_asset_id",
                schema: "inventory",
                table: "core_returns",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_core_returns_code",
                schema: "inventory",
                table: "core_returns",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_core_returns_consume_movement_id",
                schema: "inventory",
                table: "core_returns",
                column: "consume_movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_core_returns_item_id",
                schema: "inventory",
                table: "core_returns",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_core_returns_state",
                schema: "inventory",
                table: "core_returns",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_core_returns_technician_id_state",
                schema: "inventory",
                table: "core_returns",
                columns: new[] { "technician_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_core_returns_work_order_id",
                schema: "inventory",
                table: "core_returns",
                column: "work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_units_installed_asset_id_item_id",
                schema: "inventory",
                table: "serial_units",
                columns: new[] { "installed_asset_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_serial_units_item_id_serial",
                schema: "inventory",
                table: "serial_units",
                columns: new[] { "item_id", "serial" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_issues_code",
                schema: "inventory",
                table: "stock_issues",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_issues_state",
                schema: "inventory",
                table: "stock_issues",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_stock_issues_technician_id_state",
                schema: "inventory",
                table: "stock_issues",
                columns: new[] { "technician_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_returns_code",
                schema: "inventory",
                table: "stock_returns",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_returns_state",
                schema: "inventory",
                table: "stock_returns",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_stock_returns_technician_id_state",
                schema: "inventory",
                table: "stock_returns",
                columns: new[] { "technician_id", "state" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "core_returns",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "serial_units",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_issues",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_returns",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "ix_stock_movements_document_id",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "document_id",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropSequence(
                name: "core_return_code_seq",
                schema: "inventory");

            migrationBuilder.DropSequence(
                name: "stock_issue_code_seq",
                schema: "inventory");

            migrationBuilder.DropSequence(
                name: "stock_return_code_seq",
                schema: "inventory");
        }
    }
}
