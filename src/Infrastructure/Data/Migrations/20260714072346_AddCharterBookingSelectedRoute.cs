using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCharterBookingSelectedRoute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "charter_route_id",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_bookings_charter_route_id",
                table: "bookings",
                column: "charter_route_id");

            migrationBuilder.AddForeignKey(
                name: "FK_bookings_routes_charter_route_id",
                table: "bookings",
                column: "charter_route_id",
                principalTable: "routes",
                principalColumn: "route_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bookings_routes_charter_route_id",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "IX_bookings_charter_route_id",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "charter_route_id",
                table: "bookings");
        }
    }
}
