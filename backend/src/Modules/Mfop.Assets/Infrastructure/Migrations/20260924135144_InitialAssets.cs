using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mfop.Assets.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "assets");

            migrationBuilder.CreateSequence(
                name: "street_light_code_seq",
                schema: "assets");

            migrationBuilder.CreateTable(
                name: "street_lights",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    lifecycle_state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    operational_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    address_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    circuit_ref = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    power_source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    pole_material = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    pole_height_m = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    luminaire_make = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    luminaire_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    luminaire_wattage_w = table.Column<int>(type: "integer", nullable: true),
                    luminaire_serial = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    solar_panel_w = table.Column<int>(type: "integer", nullable: true),
                    solar_battery_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    solar_battery_ah = table.Column<int>(type: "integer", nullable: true),
                    solar_controller_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    installed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    installed_on_precision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    warranty_until = table.Column<DateOnly>(type: "date", nullable: true),
                    supplier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    responsible_party = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    responsible_entity_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    observed_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    capture_channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    current_tag_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_version_no = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_street_lights", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "asset_versions",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    snapshot_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_versions_street_lights_asset_id",
                        column: x => x.asset_id,
                        principalSchema: "assets",
                        principalTable: "street_lights",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "registrations",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registrar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    project_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    capture_channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    return_count = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_registrations_street_lights_asset_id",
                        column: x => x.asset_id,
                        principalSchema: "assets",
                        principalTable: "street_lights",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    check_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    origin = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    bound_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                    table.ForeignKey(
                        name: "fk_tags_street_lights_bound_asset_id",
                        column: x => x.bound_asset_id,
                        principalSchema: "assets",
                        principalTable: "street_lights",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_versions_asset_id_version_no",
                schema: "assets",
                table: "asset_versions",
                columns: new[] { "asset_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_registrations_asset_id",
                schema: "assets",
                table: "registrations",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_registrations_registrar_id",
                schema: "assets",
                table: "registrations",
                column: "registrar_id");

            migrationBuilder.CreateIndex(
                name: "ix_registrations_state",
                schema: "assets",
                table: "registrations",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_street_lights_asset_code",
                schema: "assets",
                table: "street_lights",
                column: "asset_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_street_lights_lifecycle_state",
                schema: "assets",
                table: "street_lights",
                column: "lifecycle_state");

            migrationBuilder.CreateIndex(
                name: "ix_tags_bound_asset_id",
                schema: "assets",
                table: "tags",
                column: "bound_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_tags_code",
                schema: "assets",
                table: "tags",
                column: "code",
                unique: true);

            // Spatial column derived from latitude/longitude (PostGIS; used for maps/proximity from M7/M8).
            migrationBuilder.Sql("""
                ALTER TABLE assets.street_lights
                  ADD COLUMN location geometry(Point, 4326)
                  GENERATED ALWAYS AS (ST_SetSRID(ST_MakePoint(longitude::double precision, latitude::double precision), 4326)) STORED;
                CREATE INDEX ix_street_lights_location ON assets.street_lights USING gist (location);
                """);

            // Approved versions are append-only (guard against bugs/accidents; tampering is detected by audit-verify).
            migrationBuilder.Sql("""
                CREATE FUNCTION assets.reject_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'assets.% is append-only: % rejected', TG_TABLE_NAME, TG_OP;
                END $$;
                CREATE TRIGGER asset_versions_no_update_delete BEFORE UPDATE OR DELETE ON assets.asset_versions
                  FOR EACH ROW EXECUTE FUNCTION assets.reject_mutation();
                CREATE TRIGGER asset_versions_no_truncate BEFORE TRUNCATE ON assets.asset_versions
                  FOR EACH STATEMENT EXECUTE FUNCTION assets.reject_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_versions",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "registrations",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "street_lights",
                schema: "assets");

            migrationBuilder.DropSequence(
                name: "street_light_code_seq",
                schema: "assets");
        }
    }
}
