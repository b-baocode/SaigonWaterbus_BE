using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFareMatrixRenameBoat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bookings_vessels_vessel_id",
                table: "bookings");

            migrationBuilder.DropForeignKey(
                name: "FK_seats_vessels_vessel_id",
                table: "seats");

            migrationBuilder.DropForeignKey(
                name: "FK_trips_vessels_vessel_id",
                table: "trips");

            migrationBuilder.DropPrimaryKey(
                name: "PK_vessels",
                table: "vessels");

            migrationBuilder.DropTable(
                name: "fare_rules");

            migrationBuilder.Sql(
                "UPDATE services SET booking_mode = 'BoatRental' WHERE booking_mode = 'VesselRental';");

            migrationBuilder.RenameTable(
                name: "vessels",
                newName: "boats");

            migrationBuilder.RenameColumn(
                name: "vessel_id",
                table: "boats",
                newName: "boat_id");

            migrationBuilder.RenameColumn(
                name: "vessel_code",
                table: "boats",
                newName: "boat_code");

            migrationBuilder.RenameColumn(
                name: "vessel_name",
                table: "boats",
                newName: "boat_name");

            migrationBuilder.RenameIndex(
                name: "IX_vessels_registration_number",
                table: "boats",
                newName: "IX_boats_registration_number");

            migrationBuilder.RenameIndex(
                name: "IX_vessels_status",
                table: "boats",
                newName: "IX_boats_status");

            migrationBuilder.RenameIndex(
                name: "IX_vessels_vessel_code",
                table: "boats",
                newName: "IX_boats_boat_code");

            migrationBuilder.RenameColumn(
                name: "vessel_id",
                table: "trips",
                newName: "boat_id");

            migrationBuilder.RenameIndex(
                name: "IX_trips_vessel_id",
                table: "trips",
                newName: "IX_trips_boat_id");

            migrationBuilder.RenameColumn(
                name: "vessel_id",
                table: "seats",
                newName: "boat_id");

            migrationBuilder.RenameIndex(
                name: "IX_seats_vessel_id_seat_code",
                table: "seats",
                newName: "IX_seats_boat_id_seat_code");

            migrationBuilder.RenameIndex(
                name: "IX_seats_vessel_id_deck_number_seat_row_seat_column",
                table: "seats",
                newName: "IX_seats_boat_id_deck_number_seat_row_seat_column");

            migrationBuilder.RenameColumn(
                name: "vessel_requirements",
                table: "bookings",
                newName: "boat_requirements");

            migrationBuilder.RenameColumn(
                name: "vessel_id",
                table: "bookings",
                newName: "boat_id");

            migrationBuilder.RenameIndex(
                name: "ux_bookings_vessel_date_active",
                table: "bookings",
                newName: "ux_bookings_boat_date_active");

            migrationBuilder.AddPrimaryKey(
                name: "PK_boats",
                table: "boats",
                column: "boat_id");

            migrationBuilder.AddForeignKey(
                name: "FK_bookings_boats_boat_id",
                table: "bookings",
                column: "boat_id",
                principalTable: "boats",
                principalColumn: "boat_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_seats_boats_boat_id",
                table: "seats",
                column: "boat_id",
                principalTable: "boats",
                principalColumn: "boat_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_trips_boats_boat_id",
                table: "trips",
                column: "boat_id",
                principalTable: "boats",
                principalColumn: "boat_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bookings_boats_boat_id",
                table: "bookings");

            migrationBuilder.DropForeignKey(
                name: "FK_seats_boats_boat_id",
                table: "seats");

            migrationBuilder.DropForeignKey(
                name: "FK_trips_boats_boat_id",
                table: "trips");

            migrationBuilder.DropPrimaryKey(
                name: "PK_boats",
                table: "boats");

            migrationBuilder.RenameColumn(
                name: "boat_id",
                table: "trips",
                newName: "vessel_id");

            migrationBuilder.RenameIndex(
                name: "IX_trips_boat_id",
                table: "trips",
                newName: "IX_trips_vessel_id");

            migrationBuilder.RenameColumn(
                name: "boat_id",
                table: "seats",
                newName: "vessel_id");

            migrationBuilder.RenameIndex(
                name: "IX_seats_boat_id_seat_code",
                table: "seats",
                newName: "IX_seats_vessel_id_seat_code");

            migrationBuilder.RenameIndex(
                name: "IX_seats_boat_id_deck_number_seat_row_seat_column",
                table: "seats",
                newName: "IX_seats_vessel_id_deck_number_seat_row_seat_column");

            migrationBuilder.RenameColumn(
                name: "boat_requirements",
                table: "bookings",
                newName: "vessel_requirements");

            migrationBuilder.RenameColumn(
                name: "boat_id",
                table: "bookings",
                newName: "vessel_id");

            migrationBuilder.RenameIndex(
                name: "ux_bookings_boat_date_active",
                table: "bookings",
                newName: "ux_bookings_vessel_date_active");

            migrationBuilder.RenameColumn(
                name: "boat_id",
                table: "boats",
                newName: "vessel_id");

            migrationBuilder.RenameColumn(
                name: "boat_code",
                table: "boats",
                newName: "vessel_code");

            migrationBuilder.RenameColumn(
                name: "boat_name",
                table: "boats",
                newName: "vessel_name");

            migrationBuilder.RenameIndex(
                name: "IX_boats_registration_number",
                table: "boats",
                newName: "IX_vessels_registration_number");

            migrationBuilder.RenameIndex(
                name: "IX_boats_status",
                table: "boats",
                newName: "IX_vessels_status");

            migrationBuilder.RenameIndex(
                name: "IX_boats_boat_code",
                table: "boats",
                newName: "IX_vessels_vessel_code");

            migrationBuilder.RenameTable(
                name: "boats",
                newName: "vessels");

            migrationBuilder.AddPrimaryKey(
                name: "PK_vessels",
                table: "vessels",
                column: "vessel_id");

            migrationBuilder.CreateTable(
                name: "fare_rules",
                columns: table => new
                {
                    fare_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    service_period = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fare_rules", x => x.fare_rule_id);
                    table.ForeignKey(
                        name: "FK_fare_rules_routes_route_id",
                        column: x => x.route_id,
                        principalTable: "routes",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fare_rules_stations_from_station_id",
                        column: x => x.from_station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fare_rules_stations_to_station_id",
                        column: x => x.to_station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fare_rules_from_station_id",
                table: "fare_rules",
                column: "from_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_fare_rules_route_id_from_station_id_to_station_id",
                table: "fare_rules",
                columns: new[] { "route_id", "from_station_id", "to_station_id" },
                unique: true,
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "IX_fare_rules_to_station_id",
                table: "fare_rules",
                column: "to_station_id");

            migrationBuilder.Sql(
                "UPDATE services SET booking_mode = 'VesselRental' WHERE booking_mode = 'BoatRental';");

            migrationBuilder.AddForeignKey(
                name: "FK_bookings_vessels_vessel_id",
                table: "bookings",
                column: "vessel_id",
                principalTable: "vessels",
                principalColumn: "vessel_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_seats_vessels_vessel_id",
                table: "seats",
                column: "vessel_id",
                principalTable: "vessels",
                principalColumn: "vessel_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_trips_vessels_vessel_id",
                table: "trips",
                column: "vessel_id",
                principalTable: "vessels",
                principalColumn: "vessel_id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
