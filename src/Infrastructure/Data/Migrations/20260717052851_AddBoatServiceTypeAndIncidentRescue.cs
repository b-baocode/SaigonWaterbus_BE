using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBoatServiceTypeAndIncidentRescue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "rescue_boat_id",
                table: "incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rescue_dispatched_at",
                table: "incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "rescue_dispatched_by_user_id",
                table: "incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "service_type",
                table: "boats",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Passenger");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_rescue_boat_id",
                table: "incidents",
                column: "rescue_boat_id");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_rescue_dispatched_by_user_id",
                table: "incidents",
                column: "rescue_dispatched_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_boats_service_type",
                table: "boats",
                column: "service_type");

            migrationBuilder.AddForeignKey(
                name: "FK_incidents_boats_rescue_boat_id",
                table: "incidents",
                column: "rescue_boat_id",
                principalTable: "boats",
                principalColumn: "boat_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_incidents_users_rescue_dispatched_by_user_id",
                table: "incidents",
                column: "rescue_dispatched_by_user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_incidents_boats_rescue_boat_id",
                table: "incidents");

            migrationBuilder.DropForeignKey(
                name: "FK_incidents_users_rescue_dispatched_by_user_id",
                table: "incidents");

            migrationBuilder.DropIndex(
                name: "IX_incidents_rescue_boat_id",
                table: "incidents");

            migrationBuilder.DropIndex(
                name: "IX_incidents_rescue_dispatched_by_user_id",
                table: "incidents");

            migrationBuilder.DropIndex(
                name: "IX_boats_service_type",
                table: "boats");

            migrationBuilder.DropColumn(
                name: "rescue_boat_id",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "rescue_dispatched_at",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "rescue_dispatched_by_user_id",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "service_type",
                table: "boats");
        }
    }
}
