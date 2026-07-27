using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedPricingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE seat_types
                SET base_price = 0
                WHERE is_active = false;
                """);

            migrationBuilder.Sql("""
                UPDATE charter_boat_rental_price_policies
                SET unit_price = 0
                WHERE is_active = false;
                """);

            migrationBuilder.Sql("""
                DELETE FROM fare_policies
                WHERE is_active = false;
                """);

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "seat_types");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "fare_policies");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "charter_boat_rental_price_policies");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "boats");

            migrationBuilder.DropColumn(
                name: "daily_rental_price",
                table: "boats");

            migrationBuilder.DropColumn(
                name: "hourly_rental_price",
                table: "boats");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "seat_types",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "fare_policies",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "charter_boat_rental_price_policies",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "boats",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "VND");

            migrationBuilder.AddColumn<decimal>(
                name: "daily_rental_price",
                table: "boats",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "hourly_rental_price",
                table: "boats",
                type: "numeric(12,2)",
                nullable: true);
        }
    }
}
