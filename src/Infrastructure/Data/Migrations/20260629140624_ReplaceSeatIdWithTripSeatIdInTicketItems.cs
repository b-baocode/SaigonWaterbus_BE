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
