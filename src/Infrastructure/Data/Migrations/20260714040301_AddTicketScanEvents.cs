using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketScanEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ticket_scan_events",
                columns: table => new
                {
                    ticket_scan_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    performed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_work_assignment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    boat_id = table.Column<Guid>(type: "uuid", nullable: true),
                    station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    result = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    client_operation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    device_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    server_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    scanned_code_or_token = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ticket_status_before = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ticket_status_after = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_scan_events", x => x.ticket_scan_event_id);
                    table.ForeignKey(
                        name: "FK_ticket_scan_events_boats_boat_id",
                        column: x => x.boat_id,
                        principalTable: "boats",
                        principalColumn: "boat_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ticket_scan_events_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "booking_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ticket_scan_events_staff_work_assignments_staff_work_assign~",
                        column: x => x.staff_work_assignment_id,
                        principalTable: "staff_work_assignments",
                        principalColumn: "staff_work_assignment_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ticket_scan_events_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ticket_scan_events_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "ticket_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ticket_scan_events_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ticket_scan_events_users_performed_by_user_id",
                        column: x => x.performed_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_scan_events_boat_id",
                table: "ticket_scan_events",
                column: "boat_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_scan_events_booking_id",
                table: "ticket_scan_events",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_scan_events_client_operation_id",
                table: "ticket_scan_events",
                column: "client_operation_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_scan_events_performed_by_user_id_server_time",
                table: "ticket_scan_events",
                columns: new[] { "performed_by_user_id", "server_time" });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_scan_events_server_time",
                table: "ticket_scan_events",
                column: "server_time");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_scan_events_staff_work_assignment_id",
                table: "ticket_scan_events",
                column: "staff_work_assignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_scan_events_station_id",
                table: "ticket_scan_events",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_scan_events_ticket_id_server_time",
                table: "ticket_scan_events",
                columns: new[] { "ticket_id", "server_time" });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_scan_events_trip_id_server_time",
                table: "ticket_scan_events",
                columns: new[] { "trip_id", "server_time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_scan_events");
        }
    }
}
