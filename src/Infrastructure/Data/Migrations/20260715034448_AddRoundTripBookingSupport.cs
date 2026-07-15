using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoundTripBookingSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "return_trip_id",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "trip_id",
                table: "booking_passengers",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE booking_passengers bp SET trip_id = b.trip_id
                FROM bookings b
                WHERE bp.booking_id = b.booking_id AND b.trip_id IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_bookings_return_trip_id",
                table: "bookings",
                column: "return_trip_id");

            migrationBuilder.CreateIndex(
                name: "IX_booking_passengers_trip_id",
                table: "booking_passengers",
                column: "trip_id");

            migrationBuilder.AddForeignKey(
                name: "FK_booking_passengers_trips_trip_id",
                table: "booking_passengers",
                column: "trip_id",
                principalTable: "trips",
                principalColumn: "trip_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_bookings_trips_return_trip_id",
                table: "bookings",
                column: "return_trip_id",
                principalTable: "trips",
                principalColumn: "trip_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_booking_passengers_trips_trip_id",
                table: "booking_passengers");

            migrationBuilder.DropForeignKey(
                name: "FK_bookings_trips_return_trip_id",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "IX_bookings_return_trip_id",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "IX_booking_passengers_trip_id",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "return_trip_id",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "trip_id",
                table: "booking_passengers");
        }
    }
}
