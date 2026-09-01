using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260901100000_AddCharterPassengerSeatReservation")]
public partial class AddCharterPassengerSeatReservation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "charter_seat_id",
            table: "booking_passengers",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_booking_passengers_booking_id_charter_seat_id",
            table: "booking_passengers",
            columns: new[] { "booking_id", "charter_seat_id" },
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_booking_passengers_seats_charter_seat_id",
            table: "booking_passengers",
            column: "charter_seat_id",
            principalTable: "seats",
            principalColumn: "seat_id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_booking_passengers_seats_charter_seat_id",
            table: "booking_passengers");

        migrationBuilder.DropIndex(
            name: "IX_booking_passengers_booking_id_charter_seat_id",
            table: "booking_passengers");

        migrationBuilder.DropColumn(
            name: "charter_seat_id",
            table: "booking_passengers");
    }
}
