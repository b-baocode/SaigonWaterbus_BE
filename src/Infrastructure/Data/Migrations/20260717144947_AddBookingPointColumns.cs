using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingPointColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "points_earned",
                table: "bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "points_used",
                table: "bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "points_earned",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "points_used",
                table: "bookings");
        }
    }
}
