using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripSeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trip_seats",
                columns: table => new
                {
                    trip_seat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trip_seats", x => x.trip_seat_id);
                    table.ForeignKey(
                        name: "FK_trip_seats_seats_seat_id",
                        column: x => x.seat_id,
                        principalTable: "seats",
                        principalColumn: "seat_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_trip_seats_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trip_seats_seat_id",
                table: "trip_seats",
                column: "seat_id");

            migrationBuilder.CreateIndex(
                name: "IX_trip_seats_trip_id_seat_id",
                table: "trip_seats",
                columns: new[] { "trip_id", "seat_id" },
                unique: true);

            migrationBuilder.Sql(
                @"INSERT INTO trip_seats (trip_seat_id, trip_id, seat_id, status)
                  SELECT
                      md5(t.trip_id::text || ':' || s.seat_id::text)::uuid,
                      t.trip_id,
                      s.seat_id,
                      CASE
                          WHEN EXISTS (
                              SELECT 1
                              FROM bookings AS b
                              INNER JOIN booking_passengers AS bp ON bp.booking_id = b.booking_id
                              WHERE b.trip_id = t.trip_id
                                AND bp.seat_id = s.seat_id
                                AND b.status NOT IN ('Cancelled', 'Expired', 'Refunded')
                          ) THEN 'Booked'
                          ELSE 'Available'
                      END
                  FROM trips AS t
                  INNER JOIN seats AS s ON s.boat_id = t.boat_id
                  WHERE t.boat_id IS NOT NULL
                    AND s.is_active
                  ON CONFLICT (trip_id, seat_id) DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trip_seats");
        }
    }
}
