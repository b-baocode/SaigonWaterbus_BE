using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentMissionEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "estimated_towing_minutes",
                table: "incidents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mission_status",
                table: "incidents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "IncidentCreated");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "passenger_transfer_completed_at",
                table: "incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "replacement_arrived_at",
                table: "incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rescue_arrived_at",
                table: "incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "towing_completed_at",
                table: "incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "towing_started_at",
                table: "incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "incident_mission_events",
                columns: table => new
                {
                    incident_mission_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gps_event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(50)", maxLength: 50, nullable: false),
                    boat_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    station_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reported_previous_mission_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    estimated_towing_minutes = table.Column<int>(type: "integer", nullable: true),
                    previous_mission_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    mission_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_mission_events", x => x.incident_mission_event_id);
                    table.ForeignKey(
                        name: "FK_incident_mission_events_incidents_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incidents",
                        principalColumn: "incident_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_incident_mission_events_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_mission_status",
                table: "incidents",
                column: "mission_status");

            migrationBuilder.CreateIndex(
                name: "IX_incident_mission_events_event",
                table: "incident_mission_events",
                column: "event");

            migrationBuilder.CreateIndex(
                name: "IX_incident_mission_events_incident_id_gps_event_id",
                table: "incident_mission_events",
                columns: new[] { "incident_id", "gps_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_incident_mission_events_occurred_at",
                table: "incident_mission_events",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "IX_incident_mission_events_station_id",
                table: "incident_mission_events",
                column: "station_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incident_mission_events");

            migrationBuilder.DropIndex(
                name: "IX_incidents_mission_status",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "estimated_towing_minutes",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "mission_status",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "passenger_transfer_completed_at",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "replacement_arrived_at",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "rescue_arrived_at",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "towing_completed_at",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "towing_started_at",
                table: "incidents");
        }
    }
}
