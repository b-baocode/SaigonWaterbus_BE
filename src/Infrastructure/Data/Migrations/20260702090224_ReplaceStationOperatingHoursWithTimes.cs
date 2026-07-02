using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceStationOperatingHoursWithTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "operating_hours",
                table: "stations");

            migrationBuilder.DropColumn(
                name: "station_type",
                table: "stations");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "closing_time",
                table: "stations",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "opening_time",
                table: "stations",
                type: "time without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "closing_time",
                table: "stations");

            migrationBuilder.DropColumn(
                name: "opening_time",
                table: "stations");

            migrationBuilder.AddColumn<string>(
                name: "operating_hours",
                table: "stations",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "station_type",
                table: "stations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Main");
        }
    }
}
