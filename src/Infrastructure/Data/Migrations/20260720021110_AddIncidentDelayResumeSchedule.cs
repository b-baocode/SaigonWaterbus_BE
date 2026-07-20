using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentDelayResumeSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "adjusted_arrival_time",
                table: "trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "adjusted_departure_time",
                table: "trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "delay_minutes",
                table: "trips",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "delay_reason",
                table: "trips",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "adjusted_arrival_time",
                table: "trip_stops",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "adjusted_departure_time",
                table: "trip_stops",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "replacement_delay_minutes",
                table: "incidents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "replacement_estimated_resume_at",
                table: "incidents",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "adjusted_arrival_time",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "adjusted_departure_time",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "delay_minutes",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "delay_reason",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "adjusted_arrival_time",
                table: "trip_stops");

            migrationBuilder.DropColumn(
                name: "adjusted_departure_time",
                table: "trip_stops");

            migrationBuilder.DropColumn(
                name: "replacement_delay_minutes",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "replacement_estimated_resume_at",
                table: "incidents");
        }
    }
}
