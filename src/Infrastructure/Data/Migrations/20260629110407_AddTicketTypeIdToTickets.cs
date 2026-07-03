using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketTypeIdToTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ticket_type_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tickets_ticket_type_id",
                table: "tickets",
                column: "ticket_type_id");

            migrationBuilder.AddForeignKey(
                name: "FK_tickets_ticket_types_ticket_type_id",
                table: "tickets",
                column: "ticket_type_id",
                principalTable: "ticket_types",
                principalColumn: "ticket_type_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tickets_ticket_types_ticket_type_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_ticket_type_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "ticket_type_id",
                table: "tickets");
        }
    }
}
