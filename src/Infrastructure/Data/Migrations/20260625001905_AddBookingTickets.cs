using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tickets",
                columns: table => new
                {
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_passenger_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ticket_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    qr_token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    checked_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checked_in_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tickets", x => x.ticket_id);
                    table.ForeignKey(
                        name: "FK_tickets_booking_passengers_booking_passenger_id",
                        column: x => x.booking_passenger_id,
                        principalTable: "booking_passengers",
                        principalColumn: "booking_passenger_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tickets_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "booking_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tickets_users_checked_in_by_user_id",
                        column: x => x.checked_in_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_booking_id",
                table: "tickets",
                column: "booking_id",
                unique: true,
                filter: "\"booking_passenger_id\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_booking_passenger_id",
                table: "tickets",
                column: "booking_passenger_id",
                unique: true,
                filter: "\"booking_passenger_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_checked_in_by_user_id",
                table: "tickets",
                column: "checked_in_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_qr_token",
                table: "tickets",
                column: "qr_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tickets_ticket_code",
                table: "tickets",
                column: "ticket_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tickets");
        }
    }
}
