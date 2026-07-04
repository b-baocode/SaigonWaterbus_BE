using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGpsTrackingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gps_devices",
                columns: table => new
                {
                    gps_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    boat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_sequence = table.Column<long>(type: "bigint", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gps_devices", x => x.gps_device_id);
                    table.ForeignKey(
                        name: "FK_gps_devices_boats_boat_id",
                        column: x => x.boat_id,
                        principalTable: "boats",
                        principalColumn: "boat_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "boat_latest_locations",
                columns: table => new
                {
                    boat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gps_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_boat_latest_locations", x => x.boat_id);
                    table.ForeignKey(
                        name: "FK_boat_latest_locations_boats_boat_id",
                        column: x => x.boat_id,
                        principalTable: "boats",
                        principalColumn: "boat_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_boat_latest_locations_gps_devices_gps_device_id",
                        column: x => x.gps_device_id,
                        principalTable: "gps_devices",
                        principalColumn: "gps_device_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_boat_latest_locations_routes_route_id",
                        column: x => x.route_id,
                        principalTable: "routes",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_boat_latest_locations_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_boat_latest_locations_gps_device_id",
                table: "boat_latest_locations",
                column: "gps_device_id");

            migrationBuilder.CreateIndex(
                name: "IX_boat_latest_locations_recorded_at",
                table: "boat_latest_locations",
                column: "recorded_at");

            migrationBuilder.CreateIndex(
                name: "IX_boat_latest_locations_route_id",
                table: "boat_latest_locations",
                column: "route_id");

            migrationBuilder.CreateIndex(
                name: "IX_boat_latest_locations_route_id_status",
                table: "boat_latest_locations",
                columns: new[] { "route_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_boat_latest_locations_trip_id",
                table: "boat_latest_locations",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "IX_gps_devices_boat_id_is_active",
                table: "gps_devices",
                columns: new[] { "boat_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_gps_devices_device_id",
                table: "gps_devices",
                column: "device_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "boat_latest_locations");

            migrationBuilder.DropTable(
                name: "gps_devices");
        }
    }
}
