using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewCustomerTripUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_reviews_customer_user_id",
                table: "reviews");

            migrationBuilder.CreateIndex(
                name: "ux_reviews_customer_trip",
                table: "reviews",
                columns: new[] { "customer_user_id", "trip_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_reviews_customer_trip",
                table: "reviews");

            migrationBuilder.CreateIndex(
                name: "IX_reviews_customer_user_id",
                table: "reviews",
                column: "customer_user_id");
        }
    }
}
