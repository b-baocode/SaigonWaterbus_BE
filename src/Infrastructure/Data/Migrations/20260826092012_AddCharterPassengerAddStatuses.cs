using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260826092012_AddCharterPassengerAddStatuses")]
    public class AddCharterPassengerAddStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_bookings_boat_date_active",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "ux_bookings_boat_date_active",
                table: "bookings",
                columns: new[] { "boat_id", "departure_date" },
                unique: true,
                filter: "booking_type = 'CharterBooking' AND status IN ('Quoted', 'Confirmed', 'PendingApproval', 'Approved', 'Pending')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_bookings_boat_date_active",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "ux_bookings_boat_date_active",
                table: "bookings",
                columns: new[] { "boat_id", "departure_date" },
                unique: true,
                filter: "booking_type = 'CharterBooking' AND status IN ('Quoted', 'Confirmed')");
        }
    }
}
