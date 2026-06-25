using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoveTicketTypeIntoTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fare_rules_ticket_types_ticket_type_id",
                table: "fare_rules");

            migrationBuilder.DropTable(
                name: "ticket_types");

            migrationBuilder.DropIndex(
                name: "IX_fare_rules_ticket_type_id",
                table: "fare_rules");

            migrationBuilder.DropColumn(
                name: "ticket_type_id",
                table: "fare_rules");

            migrationBuilder.AddColumn<string>(
                name: "ticket_type_code",
                table: "tickets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "CUSTOM_BOOKING");

            migrationBuilder.AddColumn<string>(
                name: "ticket_type_name",
                table: "tickets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Vé thuê tàu");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ticket_type_code",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "ticket_type_name",
                table: "tickets");

            migrationBuilder.AddColumn<Guid>(
                name: "ticket_type_id",
                table: "fare_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ticket_types",
                columns: table => new
                {
                    ticket_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    price_modifier = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    ticket_type_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ticket_type_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_types", x => x.ticket_type_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fare_rules_ticket_type_id",
                table: "fare_rules",
                column: "ticket_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_types_ticket_type_code",
                table: "ticket_types",
                column: "ticket_type_code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_fare_rules_ticket_types_ticket_type_id",
                table: "fare_rules",
                column: "ticket_type_id",
                principalTable: "ticket_types",
                principalColumn: "ticket_type_id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
