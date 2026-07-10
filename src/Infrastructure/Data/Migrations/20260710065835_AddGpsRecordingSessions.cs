using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGpsRecordingSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gps_tracking_sessions",
                columns: table => new
                {
                    gps_tracking_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gps_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: true),
                    route_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    route_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    planned_length_meters = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    end_station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stopped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gps_tracking_sessions", x => x.gps_tracking_session_id);
                    table.ForeignKey(
                        name: "FK_gps_tracking_sessions_boats_boat_id",
                        column: x => x.boat_id,
                        principalTable: "boats",
                        principalColumn: "boat_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_gps_tracking_sessions_gps_devices_gps_device_id",
                        column: x => x.gps_device_id,
                        principalTable: "gps_devices",
                        principalColumn: "gps_device_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gps_tracking_sessions_routes_route_id",
                        column: x => x.route_id,
                        principalTable: "routes",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_gps_tracking_sessions_stations_end_station_id",
                        column: x => x.end_station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_gps_tracking_sessions_stations_start_station_id",
                        column: x => x.start_station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_gps_tracking_sessions_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "gps_track_points",
                columns: table => new
                {
                    gps_track_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gps_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", nullable: false),
                    speed_kmh = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    heading = table.Column<int>(type: "integer", nullable: true),
                    accuracy_meters = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    direction = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    battery_percent = table.Column<int>(type: "integer", nullable: true),
                    signal_strength = table.Column<int>(type: "integer", nullable: true),
                    gps_fix_quality = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gps_track_points", x => x.gps_track_point_id);
                    table.ForeignKey(
                        name: "FK_gps_track_points_boats_boat_id",
                        column: x => x.boat_id,
                        principalTable: "boats",
                        principalColumn: "boat_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_gps_track_points_gps_devices_gps_device_id",
                        column: x => x.gps_device_id,
                        principalTable: "gps_devices",
                        principalColumn: "gps_device_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gps_track_points_gps_tracking_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "gps_tracking_sessions",
                        principalColumn: "gps_tracking_session_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_gps_track_points_routes_route_id",
                        column: x => x.route_id,
                        principalTable: "routes",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_gps_track_points_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_gps_track_points_boat_id_recorded_at",
                table: "gps_track_points",
                columns: new[] { "boat_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_gps_track_points_gps_device_id_message_id",
                table: "gps_track_points",
                columns: new[] { "gps_device_id", "message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gps_track_points_route_id",
                table: "gps_track_points",
                column: "route_id");

            migrationBuilder.CreateIndex(
                name: "IX_gps_track_points_session_id_recorded_at",
                table: "gps_track_points",
                columns: new[] { "session_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_gps_track_points_session_id_sequence",
                table: "gps_track_points",
                columns: new[] { "session_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gps_track_points_trip_id",
                table: "gps_track_points",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "IX_gps_tracking_sessions_boat_id_started_at",
                table: "gps_tracking_sessions",
                columns: new[] { "boat_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_gps_tracking_sessions_end_station_id",
                table: "gps_tracking_sessions",
                column: "end_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_gps_tracking_sessions_gps_device_id_status",
                table: "gps_tracking_sessions",
                columns: new[] { "gps_device_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_gps_tracking_sessions_route_code",
                table: "gps_tracking_sessions",
                column: "route_code");

            migrationBuilder.CreateIndex(
                name: "IX_gps_tracking_sessions_route_id",
                table: "gps_tracking_sessions",
                column: "route_id");

            migrationBuilder.CreateIndex(
                name: "IX_gps_tracking_sessions_start_station_id",
                table: "gps_tracking_sessions",
                column: "start_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_gps_tracking_sessions_trip_id",
                table: "gps_tracking_sessions",
                column: "trip_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gps_track_points");

            migrationBuilder.DropTable(
                name: "gps_tracking_sessions");
        }
    }
}
