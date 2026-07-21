using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripStopToStaffAssignmentsAndScanEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "trip_stop_id",
                table: "ticket_scan_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "trip_stop_id",
                table: "staff_work_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ticket_scan_events_trip_stop_id",
                table: "ticket_scan_events",
                column: "trip_stop_id");

            migrationBuilder.CreateIndex(
                name: "IX_staff_work_assignments_assignment_type_trip_stop_id_status",
                table: "staff_work_assignments",
                columns: new[] { "assignment_type", "trip_stop_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_staff_work_assignments_trip_stop_id",
                table: "staff_work_assignments",
                column: "trip_stop_id");

            migrationBuilder.AddForeignKey(
                name: "FK_staff_work_assignments_trip_stops_trip_stop_id",
                table: "staff_work_assignments",
                column: "trip_stop_id",
                principalTable: "trip_stops",
                principalColumn: "trip_stop_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ticket_scan_events_trip_stops_trip_stop_id",
                table: "ticket_scan_events",
                column: "trip_stop_id",
                principalTable: "trip_stops",
                principalColumn: "trip_stop_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_staff_work_assignments_trip_stops_trip_stop_id",
                table: "staff_work_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_ticket_scan_events_trip_stops_trip_stop_id",
                table: "ticket_scan_events");

            migrationBuilder.DropIndex(
                name: "IX_ticket_scan_events_trip_stop_id",
                table: "ticket_scan_events");

            migrationBuilder.DropIndex(
                name: "IX_staff_work_assignments_assignment_type_trip_stop_id_status",
                table: "staff_work_assignments");

            migrationBuilder.DropIndex(
                name: "IX_staff_work_assignments_trip_stop_id",
                table: "staff_work_assignments");

            migrationBuilder.DropColumn(
                name: "trip_stop_id",
                table: "ticket_scan_events");

            migrationBuilder.DropColumn(
                name: "trip_stop_id",
                table: "staff_work_assignments");
        }
    }
}
