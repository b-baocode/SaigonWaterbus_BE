using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCharterBookingRequestedBoatSelections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "requested_boat_count",
                table: "bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "requested_boat_types",
                table: "bookings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "requested_boat_count",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "requested_boat_types",
                table: "bookings");
        }
    }
}
