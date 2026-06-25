using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomBookingHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_bookings_vessel_id",
                table: "bookings");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "hold_expires_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_bookings_vessel_date_active",
                table: "bookings",
                columns: new[] { "vessel_id", "departure_date" },
                unique: true,
                filter: "booking_type = 'CustomBooking' AND status IN ('Quoted', 'Confirmed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_bookings_vessel_date_active",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "hold_expires_at",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_vessel_id",
                table: "bookings",
                column: "vessel_id");
        }
    }
}
