using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mfop.Evidence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "evidence");

            migrationBuilder.CreateTable(
                name: "evidence",
                schema: "evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sha256_device = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    sha256_server = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    received_bytes = table.Column<long>(type: "bigint", nullable: true),
                    mime = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    captured_by = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    announced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    preview_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    thumbnail_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    duplicate_of = table.Column<Guid>(type: "uuid", nullable: true),
                    superseded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    supersede_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidence", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_owner_type_owner_id",
                schema: "evidence",
                table: "evidence",
                columns: new[] { "owner_type", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_sha256_server",
                schema: "evidence",
                table: "evidence",
                column: "sha256_server");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_state",
                schema: "evidence",
                table: "evidence",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evidence",
                schema: "evidence");
        }
    }
}
