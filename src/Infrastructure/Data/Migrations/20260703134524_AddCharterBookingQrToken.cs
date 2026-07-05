using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCharterBookingQrToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "charter_booking_qr_token",
                table: "bookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql(
                @"UPDATE bookings
                  SET charter_booking_qr_token = 'CB' || upper(
                      md5(booking_id::text || ':' || clock_timestamp()::text || ':' || random()::text)
                      || md5(random()::text || ':' || booking_id::text))
                  WHERE booking_type = 'CharterBooking'
                    AND charter_booking_qr_token IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_charter_booking_qr_token",
                table: "bookings",
                column: "charter_booking_qr_token",
                unique: true,
                filter: "charter_booking_qr_token IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_bookings_charter_booking_qr_token",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "charter_booking_qr_token",
                table: "bookings");
        }
    }
}
