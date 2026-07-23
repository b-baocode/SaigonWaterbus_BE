using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFareAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fare_adjustments",
                columns: table => new
                {
                    fare_adjustment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: true),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    surcharge_percent = table.Column<decimal>(type: "numeric(7,2)", nullable: false),
                    rounding_step = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fare_adjustments", x => x.fare_adjustment_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fare_adjustments_date",
                table: "fare_adjustments",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_fare_adjustments_scope_date",
                table: "fare_adjustments",
                columns: new[] { "scope", "date" },
                unique: true,
                filter: "date IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fare_adjustments_weekend_scope",
                table: "fare_adjustments",
                column: "scope",
                unique: true,
                filter: "date IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fare_adjustments");
        }
    }
}
