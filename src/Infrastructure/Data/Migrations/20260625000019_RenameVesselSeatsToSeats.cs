using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameVesselSeatsToSeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_vessel_seats_vessels_vessel_id",
                table: "vessel_seats");

            migrationBuilder.DropPrimaryKey(
                name: "PK_vessel_seats",
                table: "vessel_seats");

            migrationBuilder.RenameTable(
                name: "vessel_seats",
                newName: "seats");

            migrationBuilder.RenameIndex(
                name: "IX_vessel_seats_vessel_id_seat_code",
                table: "seats",
                newName: "IX_seats_vessel_id_seat_code");

            migrationBuilder.RenameIndex(
                name: "IX_vessel_seats_vessel_id_deck_number_seat_row_seat_column",
                table: "seats",
                newName: "IX_seats_vessel_id_deck_number_seat_row_seat_column");

            migrationBuilder.AddPrimaryKey(
                name: "PK_seats",
                table: "seats",
                column: "seat_id");

            migrationBuilder.AddForeignKey(
                name: "FK_seats_vessels_vessel_id",
                table: "seats",
                column: "vessel_id",
                principalTable: "vessels",
                principalColumn: "vessel_id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_seats_vessels_vessel_id",
                table: "seats");

            migrationBuilder.DropPrimaryKey(
                name: "PK_seats",
                table: "seats");

            migrationBuilder.RenameTable(
                name: "seats",
                newName: "vessel_seats");

            migrationBuilder.RenameIndex(
                name: "IX_seats_vessel_id_seat_code",
                table: "vessel_seats",
                newName: "IX_vessel_seats_vessel_id_seat_code");

            migrationBuilder.RenameIndex(
                name: "IX_seats_vessel_id_deck_number_seat_row_seat_column",
                table: "vessel_seats",
                newName: "IX_vessel_seats_vessel_id_deck_number_seat_row_seat_column");

            migrationBuilder.AddPrimaryKey(
                name: "PK_vessel_seats",
                table: "vessel_seats",
                column: "seat_id");

            migrationBuilder.AddForeignKey(
                name: "FK_vessel_seats_vessels_vessel_id",
                table: "vessel_seats",
                column: "vessel_id",
                principalTable: "vessels",
                principalColumn: "vessel_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
