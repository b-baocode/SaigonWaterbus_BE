using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCharterBookingBoats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "charter_booking_boats",
                columns: table => new
                {
                    charter_booking_boat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boat_order = table.Column<int>(type: "integer", nullable: false),
                    seat_setup_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    chargeable_duration_value = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charter_booking_boats", x => x.charter_booking_boat_id);
                    table.ForeignKey(
                        name: "FK_charter_booking_boats_boats_boat_id",
                        column: x => x.boat_id,
                        principalTable: "boats",
                        principalColumn: "boat_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_charter_booking_boats_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "booking_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO charter_booking_boats (
                    charter_booking_boat_id,
                    booking_id,
                    boat_id,
                    boat_order,
                    seat_setup_type,
                    unit_price,
                    chargeable_duration_value,
                    subtotal_amount,
                    created_at,
                    updated_at
                )
                SELECT
                    b.booking_id,
                    b.booking_id,
                    b.boat_id,
                    1,
                    COALESCE(b.preferred_seat_setup_type, boats.seat_setup_type, 'FullStandard'),
                    CASE
                        WHEN b.rental_unit = 'Day' THEN COALESCE(boats.daily_rental_price, NULLIF(b.subtotal_amount, 0), 0)
                        WHEN b.rental_unit = 'Hour' THEN COALESCE(boats.hourly_rental_price, NULLIF(b.subtotal_amount, 0), 0)
                        ELSE COALESCE(NULLIF(b.subtotal_amount, 0), 0)
                    END,
                    COALESCE(NULLIF(b.duration_value, 0), 1),
                    b.subtotal_amount,
                    b.created_at,
                    b.updated_at
                FROM bookings b
                JOIN boats ON boats.boat_id = b.boat_id
                WHERE b.booking_type = 'CharterBooking'
                    AND b.boat_id IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1
                        FROM charter_booking_boats existing
                        WHERE existing.booking_id = b.booking_id
                    );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_charter_booking_boats_boat_id",
                table: "charter_booking_boats",
                column: "boat_id");

            migrationBuilder.CreateIndex(
                name: "IX_charter_booking_boats_booking_id_boat_id",
                table: "charter_booking_boats",
                columns: new[] { "booking_id", "boat_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_charter_booking_boats_booking_id_boat_order",
                table: "charter_booking_boats",
                columns: new[] { "booking_id", "boat_order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "charter_booking_boats");
        }
    }
}
