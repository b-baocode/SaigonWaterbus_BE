using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CollapseCharterBookingIntoBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_itinerary_stops_bookings_charter_booking_id",
                table: "itinerary_stops");

            migrationBuilder.RenameColumn(
                name: "charter_booking_id",
                table: "itinerary_stops",
                newName: "booking_id");

            migrationBuilder.RenameIndex(
                name: "IX_itinerary_stops_charter_booking_id_stop_order",
                table: "itinerary_stops",
                newName: "IX_itinerary_stops_booking_id_stop_order");

            migrationBuilder.AddForeignKey(
                name: "FK_itinerary_stops_bookings_booking_id",
                table: "itinerary_stops",
                column: "booking_id",
                principalTable: "bookings",
                principalColumn: "booking_id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_itinerary_stops_bookings_booking_id",
                table: "itinerary_stops");

            migrationBuilder.RenameColumn(
                name: "booking_id",
                table: "itinerary_stops",
                newName: "charter_booking_id");

            migrationBuilder.RenameIndex(
                name: "IX_itinerary_stops_booking_id_stop_order",
                table: "itinerary_stops",
                newName: "IX_itinerary_stops_charter_booking_id_stop_order");

            migrationBuilder.AddForeignKey(
                name: "FK_itinerary_stops_bookings_charter_booking_id",
                table: "itinerary_stops",
                column: "charter_booking_id",
                principalTable: "bookings",
                principalColumn: "booking_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
