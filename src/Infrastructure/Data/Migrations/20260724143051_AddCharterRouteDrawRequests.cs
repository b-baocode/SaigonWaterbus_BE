using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCharterRouteDrawRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "charter_route_draw_requests",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    candidate_route_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_route_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    in_progress_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    in_progress_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charter_route_draw_requests", x => x.request_id);
                    table.ForeignKey(
                        name: "FK_charter_route_draw_requests_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "booking_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_charter_route_draw_requests_routes_candidate_route_id",
                        column: x => x.candidate_route_id,
                        principalTable: "routes",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_charter_route_draw_requests_routes_result_route_id",
                        column: x => x.result_route_id,
                        principalTable: "routes",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_charter_route_draw_requests_users_in_progress_by_user_id",
                        column: x => x.in_progress_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_charter_route_draw_requests_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "charter_route_draw_request_stops",
                columns: table => new
                {
                    request_stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stop_order = table.Column<int>(type: "integer", nullable: false),
                    station_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    station_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    stay_duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charter_route_draw_request_stops", x => x.request_stop_id);
                    table.ForeignKey(
                        name: "FK_charter_route_draw_request_stops_charter_route_draw_request~",
                        column: x => x.request_id,
                        principalTable: "charter_route_draw_requests",
                        principalColumn: "request_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_charter_route_draw_request_stops_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_charter_route_draw_request_stops_request_id_stop_order",
                table: "charter_route_draw_request_stops",
                columns: new[] { "request_id", "stop_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_charter_route_draw_request_stops_station_id",
                table: "charter_route_draw_request_stops",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "ix_charter_route_draw_requests_booking_status",
                table: "charter_route_draw_requests",
                columns: new[] { "booking_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_charter_route_draw_requests_candidate_route_id",
                table: "charter_route_draw_requests",
                column: "candidate_route_id");

            migrationBuilder.CreateIndex(
                name: "ix_charter_route_draw_requests_created",
                table: "charter_route_draw_requests",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_charter_route_draw_requests_in_progress_by_user_id",
                table: "charter_route_draw_requests",
                column: "in_progress_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_charter_route_draw_requests_requested_by_user_id",
                table: "charter_route_draw_requests",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_charter_route_draw_requests_result_route_id",
                table: "charter_route_draw_requests",
                column: "result_route_id");

            migrationBuilder.CreateIndex(
                name: "ux_charter_route_draw_requests_booking_open",
                table: "charter_route_draw_requests",
                column: "booking_id",
                unique: true,
                filter: "status IN ('Pending', 'InProgress')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "charter_route_draw_request_stops");

            migrationBuilder.DropTable(
                name: "charter_route_draw_requests");
        }
    }
}
