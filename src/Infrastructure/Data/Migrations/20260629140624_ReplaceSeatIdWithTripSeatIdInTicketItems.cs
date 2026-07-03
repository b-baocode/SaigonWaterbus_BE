using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceSeatIdWithTripSeatIdInTicketItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ticket_items_seats_seat_id",
                table: "ticket_items");

            migrationBuilder.RenameColumn(
                name: "seat_id",
                table: "ticket_items",
                newName: "trip_seat_id");

            migrationBuilder.RenameIndex(
                name: "IX_ticket_items_seat_id",
                table: "ticket_items",
                newName: "IX_ticket_items_trip_seat_id");

            migrationBuilder.Sql(
                @"INSERT INTO trip_seats (trip_seat_id, trip_id, seat_id, status)
                  SELECT
                      md5(t.trip_id::text || ':' || s.seat_id::text)::uuid,
                      t.trip_id,
                      s.seat_id,
                      CASE
                          WHEN EXISTS (
                              SELECT 1
                              FROM ticket_items AS ti
                              INNER JOIN bookings AS b ON b.booking_id = ti.booking_id
                              WHERE b.trip_id = t.trip_id
                                AND ti.trip_seat_id = s.seat_id
                                AND b.status NOT IN ('Cancelled', 'Expired', 'Refunded')
                          ) THEN 'Booked'
                          ELSE 'Available'
                      END
                  FROM trips AS t
                  INNER JOIN seats AS s ON s.boat_id = t.boat_id
                  WHERE t.boat_id IS NOT NULL
                    AND s.is_active
                  ON CONFLICT (trip_id, seat_id) DO NOTHING;

                  UPDATE trip_seats AS ts
                  SET status = 'Booked'
                  FROM ticket_items AS ti
                  INNER JOIN bookings AS b ON b.booking_id = ti.booking_id
                  WHERE b.trip_id = ts.trip_id
                    AND ti.trip_seat_id = ts.seat_id
                    AND b.status NOT IN ('Cancelled', 'Expired', 'Refunded');");

            migrationBuilder.Sql(
                @"UPDATE ticket_items AS ti
                  SET trip_seat_id = ts.trip_seat_id
                  FROM bookings AS b
                  INNER JOIN trip_seats AS ts ON ts.trip_id = b.trip_id
                  WHERE ti.booking_id = b.booking_id
                    AND ti.trip_seat_id = ts.seat_id
                    AND b.trip_id IS NOT NULL;

                  UPDATE ticket_items AS ti
                  SET trip_seat_id = NULL
                  WHERE ti.trip_seat_id IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1
                        FROM trip_seats AS ts
                        WHERE ts.trip_seat_id = ti.trip_seat_id
                    );");

            migrationBuilder.AddForeignKey(
                name: "FK_ticket_items_trip_seats_trip_seat_id",
                table: "ticket_items",
                column: "trip_seat_id",
                principalTable: "trip_seats",
                principalColumn: "trip_seat_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ticket_items_trip_seats_trip_seat_id",
                table: "ticket_items");

            migrationBuilder.RenameColumn(
                name: "trip_seat_id",
                table: "ticket_items",
                newName: "seat_id");

            migrationBuilder.RenameIndex(
                name: "IX_ticket_items_trip_seat_id",
                table: "ticket_items",
                newName: "IX_ticket_items_seat_id");

            migrationBuilder.Sql(
                @"UPDATE ticket_items AS ti
                  SET seat_id = ts.seat_id
                  FROM trip_seats AS ts
                  WHERE ti.seat_id = ts.trip_seat_id;

                  UPDATE ticket_items AS ti
                  SET seat_id = NULL
                  WHERE ti.seat_id IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1
                        FROM seats AS s
                        WHERE s.seat_id = ti.seat_id
                    );");

            migrationBuilder.AddForeignKey(
                name: "FK_ticket_items_seats_seat_id",
                table: "ticket_items",
                column: "seat_id",
                principalTable: "seats",
                principalColumn: "seat_id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
