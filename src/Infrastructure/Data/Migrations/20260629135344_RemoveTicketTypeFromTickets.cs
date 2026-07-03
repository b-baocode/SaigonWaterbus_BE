using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTicketTypeFromTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tickets_ticket_types_ticket_type_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_ticket_type_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "ticket_type_code",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "ticket_type_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "ticket_type_name",
                table: "tickets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ticket_type_code",
                table: "tickets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ticket_type_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ticket_type_name",
                table: "tickets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

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
    }
}
