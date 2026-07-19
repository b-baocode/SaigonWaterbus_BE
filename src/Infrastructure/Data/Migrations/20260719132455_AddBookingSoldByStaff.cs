using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingSoldByStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "sold_by_staff_id",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_bookings_sold_by_staff_id",
                table: "bookings",
                column: "sold_by_staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_bookings_users_sold_by_staff_id",
                table: "bookings",
                column: "sold_by_staff_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bookings_users_sold_by_staff_id",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "IX_bookings_sold_by_staff_id",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "sold_by_staff_id",
                table: "bookings");
        }
    }
}
