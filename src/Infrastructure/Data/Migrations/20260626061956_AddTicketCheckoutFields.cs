using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketCheckoutFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "checked_out_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "checked_out_by_user_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tickets_checked_out_by_user_id",
                table: "tickets",
                column: "checked_out_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_tickets_users_checked_out_by_user_id",
                table: "tickets",
                column: "checked_out_by_user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tickets_users_checked_out_by_user_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_checked_out_by_user_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "checked_out_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "checked_out_by_user_id",
                table: "tickets");
        }
    }
}
