using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationScheduleDelayFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "actual_end_at",
                table: "operation_schedule_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "actual_start_at",
                table: "operation_schedule_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "adjusted_end_at",
                table: "operation_schedule_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "adjusted_start_at",
                table: "operation_schedule_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "delay_minutes",
                table: "operation_schedule_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "delay_reason",
                table: "operation_schedule_entries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operation_status",
                table: "operation_schedule_entries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_operation_schedule_entries_adjusted_start_at_adjusted_end_at",
                table: "operation_schedule_entries",
                columns: new[] { "adjusted_start_at", "adjusted_end_at" });

            migrationBuilder.CreateIndex(
                name: "IX_operation_schedule_entries_operation_status",
                table: "operation_schedule_entries",
                column: "operation_status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_operation_schedule_entries_adjusted_start_at_adjusted_end_at",
                table: "operation_schedule_entries");

            migrationBuilder.DropIndex(
                name: "IX_operation_schedule_entries_operation_status",
                table: "operation_schedule_entries");

            migrationBuilder.DropColumn(
                name: "actual_end_at",
                table: "operation_schedule_entries");

            migrationBuilder.DropColumn(
                name: "actual_start_at",
                table: "operation_schedule_entries");

            migrationBuilder.DropColumn(
                name: "adjusted_end_at",
                table: "operation_schedule_entries");

            migrationBuilder.DropColumn(
                name: "adjusted_start_at",
                table: "operation_schedule_entries");

            migrationBuilder.DropColumn(
                name: "delay_minutes",
                table: "operation_schedule_entries");

            migrationBuilder.DropColumn(
                name: "delay_reason",
                table: "operation_schedule_entries");

            migrationBuilder.DropColumn(
                name: "operation_status",
                table: "operation_schedule_entries");
        }
    }
}
