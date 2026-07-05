using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCharterBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "custom_from_station_id",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "custom_to_station_id",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "departure_date",
                table: "bookings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "duration_value",
                table: "bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "passenger_count",
                table: "bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rental_unit",
                table: "bookings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "special_requests",
                table: "bookings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "start_time",
                table: "bookings",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "vessel_id",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_bookings_custom_from_station_id",
                table: "bookings",
                column: "custom_from_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_custom_to_station_id",
                table: "bookings",
                column: "custom_to_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_vessel_id",
                table: "bookings",
                column: "vessel_id");

            migrationBuilder.AddForeignKey(
                name: "FK_bookings_stations_custom_from_station_id",
                table: "bookings",
                column: "custom_from_station_id",
                principalTable: "stations",
                principalColumn: "station_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_bookings_stations_custom_to_station_id",
                table: "bookings",
                column: "custom_to_station_id",
                principalTable: "stations",
                principalColumn: "station_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_bookings_vessels_vessel_id",
                table: "bookings",
                column: "vessel_id",
                principalTable: "vessels",
                principalColumn: "vessel_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bookings_stations_custom_from_station_id",
                table: "bookings");

            migrationBuilder.DropForeignKey(
                name: "FK_bookings_stations_custom_to_station_id",
                table: "bookings");

            migrationBuilder.DropForeignKey(
                name: "FK_bookings_vessels_vessel_id",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "IX_bookings_custom_from_station_id",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "IX_bookings_custom_to_station_id",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "IX_bookings_vessel_id",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "custom_from_station_id",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "custom_to_station_id",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "departure_date",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "duration_value",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "passenger_count",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "rental_unit",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "special_requests",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "start_time",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "vessel_id",
                table: "bookings");
        }
    }
}
