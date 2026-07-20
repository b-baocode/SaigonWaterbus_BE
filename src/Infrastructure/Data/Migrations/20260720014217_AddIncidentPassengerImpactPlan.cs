using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentPassengerImpactPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "active_ticket_count_snapshot",
                table: "incidents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "future_passenger_count_snapshot",
                table: "incidents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "onboard_passenger_count_snapshot",
                table: "incidents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "replacement_mission_type",
                table: "incidents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<Guid>(
                name: "replacement_target_station_id",
                table: "incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "replacement_target_stop_order",
                table: "incidents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_incidents_replacement_target_station_id",
                table: "incidents",
                column: "replacement_target_station_id");

            migrationBuilder.AddForeignKey(
                name: "FK_incidents_stations_replacement_target_station_id",
                table: "incidents",
                column: "replacement_target_station_id",
                principalTable: "stations",
                principalColumn: "station_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_incidents_stations_replacement_target_station_id",
                table: "incidents");

            migrationBuilder.DropIndex(
                name: "IX_incidents_replacement_target_station_id",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "active_ticket_count_snapshot",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "future_passenger_count_snapshot",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "onboard_passenger_count_snapshot",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "replacement_mission_type",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "replacement_target_station_id",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "replacement_target_stop_order",
                table: "incidents");
        }
    }
}
