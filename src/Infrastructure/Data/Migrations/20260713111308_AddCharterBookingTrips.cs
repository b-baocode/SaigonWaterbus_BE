using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCharterBookingTrips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_booking_id",
                table: "trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trip_type",
                table: "trips",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Regular");

            migrationBuilder.AddColumn<Guid>(
                name: "trip_id",
                table: "charter_booking_boats",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_trips_source_booking_id",
                table: "trips",
                column: "source_booking_id");

            migrationBuilder.CreateIndex(
                name: "IX_charter_booking_boats_trip_id",
                table: "charter_booking_boats",
                column: "trip_id");

            migrationBuilder.AddForeignKey(
                name: "FK_charter_booking_boats_trips_trip_id",
                table: "charter_booking_boats",
                column: "trip_id",
                principalTable: "trips",
                principalColumn: "trip_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_trips_bookings_source_booking_id",
                table: "trips",
                column: "source_booking_id",
                principalTable: "bookings",
                principalColumn: "booking_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_charter_booking_boats_trips_trip_id",
                table: "charter_booking_boats");

            migrationBuilder.DropForeignKey(
                name: "FK_trips_bookings_source_booking_id",
                table: "trips");

            migrationBuilder.DropIndex(
                name: "IX_trips_source_booking_id",
                table: "trips");

            migrationBuilder.DropIndex(
                name: "IX_charter_booking_boats_trip_id",
                table: "charter_booking_boats");

            migrationBuilder.DropColumn(
                name: "source_booking_id",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "trip_type",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "trip_id",
                table: "charter_booking_boats");
        }
    }
}
