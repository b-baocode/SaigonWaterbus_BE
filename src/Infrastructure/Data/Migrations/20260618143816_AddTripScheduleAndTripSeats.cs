using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripScheduleAndTripSeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "vessel_id",
                table: "trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "trip_schedules",
                columns: table => new
                {
                    trip_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vessel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    departure_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    recurrence_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    weekly_days = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    monthly_day = table.Column<int>(type: "integer", nullable: true),
                    horizon_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trip_schedules", x => x.trip_schedule_id);
                    table.ForeignKey(
                        name: "FK_trip_schedules_routes_route_id",
                        column: x => x.route_id,
                        principalTable: "routes",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trip_schedules_vessels_vessel_id",
                        column: x => x.vessel_id,
                        principalTable: "vessels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trip_seats",
                columns: table => new
                {
                    trip_seat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trip_seats", x => x.trip_seat_id);
                    table.ForeignKey(
                        name: "FK_trip_seats_seats_seat_id",
                        column: x => x.seat_id,
                        principalTable: "seats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trip_seats_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "generated_trips",
                columns: table => new
                {
                    generated_trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operating_date = table.Column<DateOnly>(type: "date", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generated_trips", x => x.generated_trip_id);
                    table.ForeignKey(
                        name: "FK_generated_trips_trip_schedules_trip_schedule_id",
                        column: x => x.trip_schedule_id,
                        principalTable: "trip_schedules",
                        principalColumn: "trip_schedule_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_generated_trips_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trips_vessel_id",
                table: "trips",
                column: "vessel_id");

            migrationBuilder.CreateIndex(
                name: "IX_generated_trips_trip_id",
                table: "generated_trips",
                column: "trip_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_generated_trips_trip_schedule_id_operating_date",
                table: "generated_trips",
                columns: new[] { "trip_schedule_id", "operating_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trip_schedules_route_id",
                table: "trip_schedules",
                column: "route_id");

            migrationBuilder.CreateIndex(
                name: "IX_trip_schedules_vessel_id",
                table: "trip_schedules",
                column: "vessel_id");

            migrationBuilder.CreateIndex(
                name: "IX_trip_seats_seat_id",
                table: "trip_seats",
                column: "seat_id");

            migrationBuilder.CreateIndex(
                name: "IX_trip_seats_trip_id_seat_id",
                table: "trip_seats",
                columns: new[] { "trip_id", "seat_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_trips_vessels_vessel_id",
                table: "trips",
                column: "vessel_id",
                principalTable: "vessels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_trips_vessels_vessel_id",
                table: "trips");

            migrationBuilder.DropTable(
                name: "generated_trips");

            migrationBuilder.DropTable(
                name: "trip_seats");

            migrationBuilder.DropTable(
                name: "trip_schedules");

            migrationBuilder.DropIndex(
                name: "IX_trips_vessel_id",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "vessel_id",
                table: "trips");
        }
    }
}
