using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomBookingRentalUnitAndTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "rental_unit",
                table: "custom_booking_requests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Day");

            migrationBuilder.CreateTable(
                name: "custom_booking_tickets",
                columns: table => new
                {
                    custom_booking_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    custom_booking_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    qr_token_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    qr_issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    qr_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    qr_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    qr_used_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_booking_tickets", x => x.custom_booking_ticket_id);
                    table.ForeignKey(
                        name: "FK_custom_booking_tickets_custom_booking_requests_custom_booki~",
                        column: x => x.custom_booking_request_id,
                        principalTable: "custom_booking_requests",
                        principalColumn: "custom_booking_request_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_custom_booking_tickets_users_qr_used_by_user_id",
                        column: x => x.qr_used_by_user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_tickets_custom_booking_request_id",
                table: "custom_booking_tickets",
                column: "custom_booking_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_tickets_qr_token_hash",
                table: "custom_booking_tickets",
                column: "qr_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_tickets_qr_used_by_user_id",
                table: "custom_booking_tickets",
                column: "qr_used_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_tickets_status",
                table: "custom_booking_tickets",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_tickets_ticket_code",
                table: "custom_booking_tickets",
                column: "ticket_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_booking_tickets");

            migrationBuilder.DropColumn(
                name: "rental_unit",
                table: "custom_booking_requests");
        }
    }
}
