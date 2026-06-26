using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOperationalServiceIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bookings_services_service_id",
                table: "bookings");

            migrationBuilder.DropForeignKey(
                name: "FK_fare_rules_services_service_id",
                table: "fare_rules");

            migrationBuilder.DropForeignKey(
                name: "FK_trips_services_service_id",
                table: "trips");

            migrationBuilder.DropIndex(
                name: "IX_trips_service_id",
                table: "trips");

            migrationBuilder.DropIndex(
                name: "IX_fare_rules_service_id",
                table: "fare_rules");

            migrationBuilder.DropIndex(
                name: "IX_bookings_service_id",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "service_id",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "service_id",
                table: "fare_rules");

            migrationBuilder.DropColumn(
                name: "service_id",
                table: "bookings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "service_id",
                table: "trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "service_id",
                table: "fare_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "service_id",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_trips_service_id",
                table: "trips",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "IX_fare_rules_service_id",
                table: "fare_rules",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_service_id",
                table: "bookings",
                column: "service_id");

            migrationBuilder.AddForeignKey(
                name: "FK_bookings_services_service_id",
                table: "bookings",
                column: "service_id",
                principalTable: "services",
                principalColumn: "service_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_fare_rules_services_service_id",
                table: "fare_rules",
                column: "service_id",
                principalTable: "services",
                principalColumn: "service_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_trips_services_service_id",
                table: "trips",
                column: "service_id",
                principalTable: "services",
                principalColumn: "service_id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
