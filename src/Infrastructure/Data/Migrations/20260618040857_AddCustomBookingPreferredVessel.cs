using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomBookingPreferredVessel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "preferred_vessel_id",
                table: "custom_booking_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_requests_preferred_vessel_id",
                table: "custom_booking_requests",
                column: "preferred_vessel_id");

            migrationBuilder.AddForeignKey(
                name: "FK_custom_booking_requests_vessels_preferred_vessel_id",
                table: "custom_booking_requests",
                column: "preferred_vessel_id",
                principalTable: "vessels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_custom_booking_requests_vessels_preferred_vessel_id",
                table: "custom_booking_requests");

            migrationBuilder.DropIndex(
                name: "IX_custom_booking_requests_preferred_vessel_id",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "preferred_vessel_id",
                table: "custom_booking_requests");
        }
    }
}
