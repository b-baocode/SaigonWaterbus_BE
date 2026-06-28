using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(global::SaigonWaterbus.Infrastructure.Data.ApplicationDbContext))]
    [Migration("20260628090000_AddTicketReissueFields")]
    public partial class AddTicketReissueFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tickets_booking_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_booking_passenger_id",
                table: "tickets");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reissued_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reissued_by_user_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reissued_from_ticket_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reissue_reason",
                table: "tickets",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tickets_booking_id",
                table: "tickets",
                column: "booking_id",
                unique: true,
                filter: "\"booking_passenger_id\" IS NULL AND \"status\" NOT IN ('Cancelled', 'Expired')");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_booking_passenger_id",
                table: "tickets",
                column: "booking_passenger_id",
                unique: true,
                filter: "\"booking_passenger_id\" IS NOT NULL AND \"status\" NOT IN ('Cancelled', 'Expired')");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_reissued_by_user_id",
                table: "tickets",
                column: "reissued_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_reissued_from_ticket_id",
                table: "tickets",
                column: "reissued_from_ticket_id");

            migrationBuilder.AddForeignKey(
                name: "FK_tickets_users_reissued_by_user_id",
                table: "tickets",
                column: "reissued_by_user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tickets_users_reissued_by_user_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_booking_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_booking_passenger_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_reissued_by_user_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_reissued_from_ticket_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "reissued_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "reissued_by_user_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "reissued_from_ticket_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "reissue_reason",
                table: "tickets");

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
        }
    }
}
