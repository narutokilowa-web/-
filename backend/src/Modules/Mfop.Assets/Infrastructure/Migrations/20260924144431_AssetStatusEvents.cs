using System;
using Mfop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mfop.Assets.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AssetStatusEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asset_status_events",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transition_id = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cause_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cause_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_status_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_status_events_street_lights_asset_id",
                        column: x => x.asset_id,
                        principalSchema: "assets",
                        principalTable: "street_lights",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_status_events_asset_id_at",
                schema: "assets",
                table: "asset_status_events",
                columns: new[] { "asset_id", "at" });

            migrationBuilder.AppendOnly("assets", "asset_status_events");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_status_events",
                schema: "assets");
        }
    }
}
