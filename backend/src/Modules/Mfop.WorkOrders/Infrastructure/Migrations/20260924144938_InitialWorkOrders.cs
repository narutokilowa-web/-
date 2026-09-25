using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mfop.WorkOrders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialWorkOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "workorders");

            migrationBuilder.CreateSequence(
                name: "work_order_code_seq",
                schema: "workorders");

            migrationBuilder.CreateTable(
                name: "work_orders",
                schema: "workorders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fault_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    priority = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sla_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assignments",
                schema: "workorders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    started_by = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_by = table.Column<Guid>(type: "uuid", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    end_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignments_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalSchema: "workorders",
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignments_work_order_id_state",
                schema: "workorders",
                table: "assignments",
                columns: new[] { "work_order_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_asset_id",
                schema: "workorders",
                table: "work_orders",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_assignee_id_state",
                schema: "workorders",
                table: "work_orders",
                columns: new[] { "assignee_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_code",
                schema: "workorders",
                table: "work_orders",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_fault_id",
                schema: "workorders",
                table: "work_orders",
                column: "fault_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_state",
                schema: "workorders",
                table: "work_orders",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignments",
                schema: "workorders");

            migrationBuilder.DropTable(
                name: "work_orders",
                schema: "workorders");

            migrationBuilder.DropSequence(
                name: "work_order_code_seq",
                schema: "workorders");
        }
    }
}
