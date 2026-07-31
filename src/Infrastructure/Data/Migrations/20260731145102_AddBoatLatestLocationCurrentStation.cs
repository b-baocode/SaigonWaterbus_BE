using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBoatLatestLocationCurrentStation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "current_station_id",
                table: "boat_latest_locations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_boat_latest_locations_current_station_id",
                table: "boat_latest_locations",
                column: "current_station_id");

            migrationBuilder.AddForeignKey(
                name: "FK_boat_latest_locations_stations_current_station_id",
                table: "boat_latest_locations",
                column: "current_station_id",
                principalTable: "stations",
                principalColumn: "station_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_boat_latest_locations_stations_current_station_id",
                table: "boat_latest_locations");

            migrationBuilder.DropIndex(
                name: "IX_boat_latest_locations_current_station_id",
                table: "boat_latest_locations");

            migrationBuilder.DropColumn(
                name: "current_station_id",
                table: "boat_latest_locations");
        }
    }
}
