using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomBookingVesselRentalService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "waterbus_service_id",
                table: "custom_booking_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_requests_waterbus_service_id",
                table: "custom_booking_requests",
                column: "waterbus_service_id");

            migrationBuilder.AddForeignKey(
                name: "FK_custom_booking_requests_waterbus_services_waterbus_service_~",
                table: "custom_booking_requests",
                column: "waterbus_service_id",
                principalTable: "waterbus_services",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_custom_booking_requests_waterbus_services_waterbus_service_~",
                table: "custom_booking_requests");

            migrationBuilder.DropIndex(
                name: "IX_custom_booking_requests_waterbus_service_id",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "waterbus_service_id",
                table: "custom_booking_requests");
        }
    }
}
