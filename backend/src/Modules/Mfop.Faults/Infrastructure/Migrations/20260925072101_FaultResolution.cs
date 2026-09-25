using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mfop.Faults.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FaultResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resolved_at",
                schema: "faults",
                table: "faults",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "resolved_by_work_order_id",
                schema: "faults",
                table: "faults",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "resolved_at",
                schema: "faults",
                table: "faults");

            migrationBuilder.DropColumn(
                name: "resolved_by_work_order_id",
                schema: "faults",
                table: "faults");
        }
    }
}
