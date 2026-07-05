using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCharterBookingCustomerRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "adult_count",
                table: "bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "child_count",
                table: "bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "preferred_number_of_decks",
                table: "bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "preferred_seat_setup_type",
                table: "bookings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vessel_requirements",
                table: "bookings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "adult_count",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "child_count",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "preferred_number_of_decks",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "preferred_seat_setup_type",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "vessel_requirements",
                table: "bookings");
        }
    }
}
