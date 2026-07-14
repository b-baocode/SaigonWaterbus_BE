using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripStops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trip_stops",
                columns: table => new
                {
                    trip_stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stop_order = table.Column<int>(type: "integer", nullable: false),
                    stay_duration_minutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    planned_arrival_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    planned_departure_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trip_stops", x => x.trip_stop_id);
                    table.ForeignKey(
                        name: "FK_trip_stops_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trip_stops_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trip_stops_station_id",
                table: "trip_stops",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "IX_trip_stops_trip_id_stop_order",
                table: "trip_stops",
                columns: new[] { "trip_id", "stop_order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trip_stops");
        }
    }
}
