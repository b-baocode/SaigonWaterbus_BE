using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_trips_boat_id",
                table: "trips");

            migrationBuilder.CreateIndex(
                name: "ix_trips_boat_operating_date_status",
                table: "trips",
                columns: new[] { "boat_id", "operating_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_trips_status_operating_date",
                table: "trips",
                columns: new[] { "status", "operating_date" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_type_customer_created",
                table: "bookings",
                columns: new[] { "booking_type", "customer_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_type_departure_boat",
                table: "bookings",
                columns: new[] { "booking_type", "departure_date", "boat_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_type_status_departure_date",
                table: "bookings",
                columns: new[] { "booking_type", "status", "departure_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_trips_boat_operating_date_status",
                table: "trips");

            migrationBuilder.DropIndex(
                name: "ix_trips_status_operating_date",
                table: "trips");

            migrationBuilder.DropIndex(
                name: "ix_bookings_type_customer_created",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "ix_bookings_type_departure_boat",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "ix_bookings_type_status_departure_date",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "IX_trips_boat_id",
                table: "trips",
                column: "boat_id");
        }
    }
}
