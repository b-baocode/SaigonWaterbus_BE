using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomBookingQrToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custom_booking_qr_token",
                table: "bookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql(
                @"UPDATE bookings
                  SET custom_booking_qr_token = 'CB' || upper(
                      md5(booking_id::text || ':' || clock_timestamp()::text || ':' || random()::text)
                      || md5(random()::text || ':' || booking_id::text))
                  WHERE booking_type = 'CustomBooking'
                    AND custom_booking_qr_token IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_custom_booking_qr_token",
                table: "bookings",
                column: "custom_booking_qr_token",
                unique: true,
                filter: "custom_booking_qr_token IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_bookings_custom_booking_qr_token",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "custom_booking_qr_token",
                table: "bookings");
        }
    }
}
