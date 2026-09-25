using System;
using Mfop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mfop.WorkOrders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FieldExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "checklist_template_id",
                schema: "workorders",
                table: "work_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at",
                schema: "workorders",
                table: "work_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "closed_by",
                schema: "workorders",
                table: "work_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "diagnosis_code",
                schema: "workorders",
                table: "work_orders",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "diagnosis_note",
                schema: "workorders",
                table: "work_orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_arrived_at",
                schema: "workorders",
                table: "work_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "last_event_seq",
                schema: "workorders",
                table: "work_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "no_material_reason",
                schema: "workorders",
                table: "work_orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "outcome",
                schema: "workorders",
                table: "work_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "rework_of_id",
                schema: "workorders",
                table: "work_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "root_cause_note",
                schema: "workorders",
                table: "work_orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seal_hash",
                schema: "workorders",
                table: "work_orders",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "submission_count",
                schema: "workorders",
                table: "work_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "submitted_at",
                schema: "workorders",
                table: "work_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "test_method",
                schema: "workorders",
                table: "work_orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "test_result",
                schema: "workorders",
                table: "work_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "work_started_at",
                schema: "workorders",
                table: "work_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "checklist_templates",
                schema: "workorders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    template_version = table.Column<int>(type: "integer", nullable: false),
                    power_sources = table.Column<string[]>(type: "text[]", nullable: false),
                    items = table.Column<string>(type: "jsonb", nullable: false),
                    test_methods = table.Column<string>(type: "jsonb", nullable: false),
                    evidence_requirements = table.Column<string>(type: "jsonb", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklist_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "diagnosis_codes",
                schema: "workorders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    counts_as_failure = table.Column<bool>(type: "boolean", nullable: false),
                    requires_material = table.Column<bool>(type: "boolean", nullable: false),
                    expected_items = table.Column<string[]>(type: "text[]", nullable: false),
                    power_sources = table.Column<string[]>(type: "text[]", nullable: false),
                    path = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diagnosis_codes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wo_arrivals",
                schema: "workorders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    arrival_no = table.Column<int>(type: "integer", nullable: false),
                    tag_scanned = table.Column<bool>(type: "boolean", nullable: false),
                    scanned_code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    manual_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    manual_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    device_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wo_arrivals", x => x.id);
                    table.ForeignKey(
                        name: "fk_wo_arrivals_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalSchema: "workorders",
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wo_events",
                schema: "workorders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    device_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wo_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_wo_events_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalSchema: "workorders",
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wo_submissions",
                schema: "workorders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_no = table.Column<int>(type: "integer", nullable: false),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    report = table.Column<string>(type: "jsonb", nullable: false),
                    payload_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wo_submissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_wo_submissions_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalSchema: "workorders",
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "checklist_results",
                schema: "workorders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    not_tested_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    answers = table.Column<string>(type: "jsonb", nullable: false),
                    test_result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    blocked = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklist_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_checklist_results_checklist_templates_template_id",
                        column: x => x.template_id,
                        principalSchema: "workorders",
                        principalTable: "checklist_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_checklist_results_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalSchema: "workorders",
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wo_reviews",
                schema: "workorders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wo_reviews", x => x.id);
                    table.ForeignKey(
                        name: "fk_wo_reviews_wo_submissions_submission_id",
                        column: x => x.submission_id,
                        principalSchema: "workorders",
                        principalTable: "wo_submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_wo_reviews_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalSchema: "workorders",
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_checklist_results_template_id",
                schema: "workorders",
                table: "checklist_results",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_results_work_order_id",
                schema: "workorders",
                table: "checklist_results",
                column: "work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_code_template_version",
                schema: "workorders",
                table: "checklist_templates",
                columns: new[] { "code", "template_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_diagnosis_codes_code",
                schema: "workorders",
                table: "diagnosis_codes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wo_arrivals_work_order_id_arrival_no",
                schema: "workorders",
                table: "wo_arrivals",
                columns: new[] { "work_order_id", "arrival_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wo_events_work_order_id_seq",
                schema: "workorders",
                table: "wo_events",
                columns: new[] { "work_order_id", "seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wo_reviews_submission_id",
                schema: "workorders",
                table: "wo_reviews",
                column: "submission_id");

            migrationBuilder.CreateIndex(
                name: "ix_wo_reviews_work_order_id",
                schema: "workorders",
                table: "wo_reviews",
                column: "work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_wo_submissions_work_order_id_submission_no",
                schema: "workorders",
                table: "wo_submissions",
                columns: new[] { "work_order_id", "submission_no" },
                unique: true);

            migrationBuilder.AppendOnly("workorders", "wo_events");
            migrationBuilder.AppendOnly("workorders", "wo_arrivals");
            migrationBuilder.AppendOnly("workorders", "checklist_templates");
            migrationBuilder.AppendOnly("workorders", "checklist_results");
            migrationBuilder.AppendOnly("workorders", "wo_submissions");
            migrationBuilder.AppendOnly("workorders", "wo_reviews");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checklist_results",
                schema: "workorders");

            migrationBuilder.DropTable(
                name: "diagnosis_codes",
                schema: "workorders");

            migrationBuilder.DropTable(
                name: "wo_arrivals",
                schema: "workorders");

            migrationBuilder.DropTable(
                name: "wo_events",
                schema: "workorders");

            migrationBuilder.DropTable(
                name: "wo_reviews",
                schema: "workorders");

            migrationBuilder.DropTable(
                name: "checklist_templates",
                schema: "workorders");

            migrationBuilder.DropTable(
                name: "wo_submissions",
                schema: "workorders");

            migrationBuilder.DropColumn(
                name: "checklist_template_id",
                schema: "workorders",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "closed_at",
                schema: "workorders",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "closed_by",
                schema: "workorders",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "diagnosis_code",
                schema: "workorders",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "diagnosis_note",
                schema: "workorders",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "first_arrived_at",
                schema: "workorders",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "last_event_seq",
                schema: "workorders",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "no_material_reason",
                schema: "workorders",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "outcome",
                schema: "workorders",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "rework_of_id",
                schema: "workorders",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "root_cause_note",
                schema: "workorders",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "seal_hash",
                schema: "workorders",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "submission_count",
                schema: "workorders",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                schema: "workorders",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "test_method",
                schema: "workorders",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "test_result",
                schema: "workorders",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "work_started_at",
                schema: "workorders",
                table: "work_orders");
        }
    }
}
