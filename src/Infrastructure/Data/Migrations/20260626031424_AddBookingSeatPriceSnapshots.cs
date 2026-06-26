using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingSeatPriceSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "seat_code",
                table: "booking_passengers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "seat_id",
                table: "booking_passengers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_price",
                table: "booking_passengers",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_booking_passengers_seat_id",
                table: "booking_passengers",
                column: "seat_id");

            migrationBuilder.AddForeignKey(
                name: "FK_booking_passengers_seats_seat_id",
                table: "booking_passengers",
                column: "seat_id",
                principalTable: "seats",
                principalColumn: "seat_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_booking_passengers_seats_seat_id",
                table: "booking_passengers");

            migrationBuilder.DropIndex(
                name: "IX_booking_passengers_seat_id",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "seat_code",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "seat_id",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "unit_price",
                table: "booking_passengers");
        }
    }
}
