using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripStopMovementStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "actual_arrival_time",
                table: "trip_stops",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "actual_departure_time",
                table: "trip_stops",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stop_status",
                table: "trip_stops",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Scheduled");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "actual_arrival_time",
                table: "trip_stops");

            migrationBuilder.DropColumn(
                name: "actual_departure_time",
                table: "trip_stops");

            migrationBuilder.DropColumn(
                name: "stop_status",
                table: "trip_stops");
        }
    }
}
