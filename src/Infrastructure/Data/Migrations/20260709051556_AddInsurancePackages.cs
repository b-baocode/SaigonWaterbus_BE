using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInsurancePackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "insurance_snapshot",
                table: "bookings",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "insurance_packages",
                columns: table => new
                {
                    insurance_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    package_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    booking_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    provider_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    provider_logo_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    unit_premium_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    coverage_amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    conditions = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "ARRAY[]::text[]"),
                    terms_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_insurance_packages", x => x.insurance_package_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_insurance_packages_booking_type_is_active",
                table: "insurance_packages",
                columns: new[] { "booking_type", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_insurance_packages_booking_type_package_code",
                table: "insurance_packages",
                columns: new[] { "booking_type", "package_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "insurance_packages");

            migrationBuilder.DropColumn(
                name: "insurance_snapshot",
                table: "bookings");
        }
    }
}
