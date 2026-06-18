using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomBookingQrTokenAndPassengerManifest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "qr_token",
                table: "custom_booking_tickets",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "passenger_manifest_completed_at",
                table: "custom_booking_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "passenger_manifest_completed_by_user_id",
                table: "custom_booking_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "passenger_manifest_status",
                table: "custom_booking_requests",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "NotStarted");

            migrationBuilder.AlterColumn<string>(
                name: "passenger_manifest_status",
                table: "custom_booking_requests",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "NotStarted");

            migrationBuilder.CreateTable(
                name: "custom_booking_passengers",
                columns: table => new
                {
                    custom_booking_passenger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    custom_booking_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    passenger_order = table.Column<int>(type: "integer", nullable: false),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    passenger_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_booking_passengers", x => x.custom_booking_passenger_id);
                    table.ForeignKey(
                        name: "FK_custom_booking_passengers_custom_booking_requests_custom_bo~",
                        column: x => x.custom_booking_request_id,
                        principalTable: "custom_booking_requests",
                        principalColumn: "custom_booking_request_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_requests_passenger_manifest_completed_by_use~",
                table: "custom_booking_requests",
                column: "passenger_manifest_completed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_passengers_custom_booking_request_id",
                table: "custom_booking_passengers",
                column: "custom_booking_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_passengers_custom_booking_request_id_passeng~",
                table: "custom_booking_passengers",
                columns: new[] { "custom_booking_request_id", "passenger_order" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_custom_booking_requests_users_passenger_manifest_completed_~",
                table: "custom_booking_requests",
                column: "passenger_manifest_completed_by_user_id",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_custom_booking_requests_users_passenger_manifest_completed_~",
                table: "custom_booking_requests");

            migrationBuilder.DropTable(
                name: "custom_booking_passengers");

            migrationBuilder.DropIndex(
                name: "IX_custom_booking_requests_passenger_manifest_completed_by_use~",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "qr_token",
                table: "custom_booking_tickets");

            migrationBuilder.DropColumn(
                name: "passenger_manifest_completed_at",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "passenger_manifest_completed_by_user_id",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "passenger_manifest_status",
                table: "custom_booking_requests");
        }
    }
}
