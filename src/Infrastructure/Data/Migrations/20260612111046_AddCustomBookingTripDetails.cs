using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomBookingTripDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "adult_count",
                table: "custom_booking_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "buffer_minutes",
                table: "custom_booking_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "child_count",
                table: "custom_booking_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "estimated_duration_minutes",
                table: "custom_booking_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "estimated_end_date",
                table: "custom_booking_requests",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "estimated_stay_minutes",
                table: "custom_booking_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "estimated_travel_minutes",
                table: "custom_booking_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "preferred_vessel_id",
                table: "custom_booking_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "custom_booking_itinerary_stops",
                columns: table => new
                {
                    custom_booking_itinerary_stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    custom_booking_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stop_order = table.Column<int>(type: "integer", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stay_duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_booking_itinerary_stops", x => x.custom_booking_itinerary_stop_id);
                    table.ForeignKey(
                        name: "FK_custom_booking_itinerary_stops_custom_booking_requests_cust~",
                        column: x => x.custom_booking_request_id,
                        principalTable: "custom_booking_requests",
                        principalColumn: "custom_booking_request_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_custom_booking_itinerary_stops_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_requests_preferred_vessel_id",
                table: "custom_booking_requests",
                column: "preferred_vessel_id");

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_itinerary_stops_custom_booking_request_id_st~",
                table: "custom_booking_itinerary_stops",
                columns: new[] { "custom_booking_request_id", "stop_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_itinerary_stops_station_id",
                table: "custom_booking_itinerary_stops",
                column: "station_id");

            migrationBuilder.AddForeignKey(
                name: "FK_custom_booking_requests_vessels_preferred_vessel_id",
                table: "custom_booking_requests",
                column: "preferred_vessel_id",
                principalTable: "vessels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_custom_booking_requests_vessels_preferred_vessel_id",
                table: "custom_booking_requests");

            migrationBuilder.DropTable(
                name: "custom_booking_itinerary_stops");

            migrationBuilder.DropIndex(
                name: "IX_custom_booking_requests_preferred_vessel_id",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "adult_count",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "buffer_minutes",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "child_count",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "estimated_duration_minutes",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "estimated_end_date",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "estimated_stay_minutes",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "estimated_travel_minutes",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "preferred_vessel_id",
                table: "custom_booking_requests");
        }
    }
}
