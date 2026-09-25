using System;
using Mfop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mfop.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "items",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    serial_tracked = table.Column<bool>(type: "boolean", nullable: false),
                    core_return_required = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_locations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    custodian_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_locations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "item_costs",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    as_of = table.Column<DateOnly>(type: "date", nullable: false),
                    set_by = table.Column<Guid>(type: "uuid", nullable: true),
                    set_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_costs", x => x.id);
                    table.ForeignKey(
                        name: "fk_item_costs_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_balances",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_balances", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_balances_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_balances_stock_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "inventory",
                        principalTable: "stock_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    from_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serials = table.Column<string[]>(type: "text[]", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_of = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    exceeds_custody = table.Column<bool>(type: "boolean", nullable: false),
                    serial_not_in_custody = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.CheckConstraint("ck_stock_movements_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_movements_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_stock_locations_from_location_id",
                        column: x => x.from_location_id,
                        principalSchema: "inventory",
                        principalTable: "stock_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_stock_locations_to_location_id",
                        column: x => x.to_location_id,
                        principalSchema: "inventory",
                        principalTable: "stock_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_item_costs_item_id_as_of",
                schema: "inventory",
                table: "item_costs",
                columns: new[] { "item_id", "as_of" });

            migrationBuilder.CreateIndex(
                name: "ix_items_sku",
                schema: "inventory",
                table: "items",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_balances_item_id",
                schema: "inventory",
                table: "stock_balances",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_balances_location_id_item_id",
                schema: "inventory",
                table: "stock_balances",
                columns: new[] { "location_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_locations_kind_custodian_user_id",
                schema: "inventory",
                table: "stock_locations",
                columns: new[] { "kind", "custodian_user_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_asset_id",
                schema: "inventory",
                table: "stock_movements",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_from_location_id",
                schema: "inventory",
                table: "stock_movements",
                column: "from_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_item_id",
                schema: "inventory",
                table: "stock_movements",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_reversal_of",
                schema: "inventory",
                table: "stock_movements",
                column: "reversal_of",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_to_location_id",
                schema: "inventory",
                table: "stock_movements",
                column: "to_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_work_order_id",
                schema: "inventory",
                table: "stock_movements",
                column: "work_order_id");

            migrationBuilder.AppendOnly("inventory", "stock_movements");
            migrationBuilder.AppendOnly("inventory", "item_costs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_costs",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_balances",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "items",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_locations",
                schema: "inventory");
        }
    }
}
