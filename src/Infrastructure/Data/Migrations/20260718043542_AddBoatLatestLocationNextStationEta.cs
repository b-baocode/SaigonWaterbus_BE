using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBoatLatestLocationNextStationEta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "next_station_id",
                table: "boat_latest_locations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "remaining_distance_km_to_next_station",
                table: "boat_latest_locations",
                type: "numeric(10,3)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "remaining_minutes_to_next_station",
                table: "boat_latest_locations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_boat_latest_locations_next_station_id",
                table: "boat_latest_locations",
                column: "next_station_id");

            migrationBuilder.AddForeignKey(
                name: "FK_boat_latest_locations_stations_next_station_id",
                table: "boat_latest_locations",
                column: "next_station_id",
                principalTable: "stations",
                principalColumn: "station_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_boat_latest_locations_stations_next_station_id",
                table: "boat_latest_locations");

            migrationBuilder.DropIndex(
                name: "IX_boat_latest_locations_next_station_id",
                table: "boat_latest_locations");

            migrationBuilder.DropColumn(
                name: "next_station_id",
                table: "boat_latest_locations");

            migrationBuilder.DropColumn(
                name: "remaining_distance_km_to_next_station",
                table: "boat_latest_locations");

            migrationBuilder.DropColumn(
                name: "remaining_minutes_to_next_station",
                table: "boat_latest_locations");
        }
    }
}
