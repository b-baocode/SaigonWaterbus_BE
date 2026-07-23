using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripStaffDelayState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delay_ended_at",
                table: "trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "delay_propagation_minutes",
                table: "trips",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "delay_start_stop_order",
                table: "trips",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delay_started_at",
                table: "trips",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "delay_ended_at",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "delay_propagation_minutes",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "delay_start_stop_order",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "delay_started_at",
                table: "trips");
        }
    }
}
