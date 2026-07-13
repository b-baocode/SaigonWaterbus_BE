using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTicketItemAndTicketType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tickets_ticket_items_ticket_item_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_booking_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_ticket_item_id",
                table: "tickets");

            migrationBuilder.AddColumn<Guid>(
                name: "booking_passenger_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                @"UPDATE tickets AS t
                  SET booking_passenger_id = ti.booking_passenger_id
                  FROM ticket_items AS ti
                  WHERE t.ticket_item_id = ti.ticket_item_id;");

            migrationBuilder.DropColumn(
                name: "ticket_item_id",
                table: "tickets");

            migrationBuilder.DropTable(
                name: "ticket_items");

            migrationBuilder.DropTable(
                name: "ticket_types");

            migrationBuilder.AddColumn<Guid>(
                name: "trip_seat_id",
                table: "booking_passengers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_price",
                table: "booking_passengers",
                type: "numeric(12,2)",
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
                name: "IX_booking_passengers_trip_seat_id",
                table: "booking_passengers",
                column: "trip_seat_id");

            migrationBuilder.AddForeignKey(
                name: "FK_tickets_booking_passengers_booking_passenger_id",
                table: "tickets",
                column: "booking_passenger_id",
                principalTable: "booking_passengers",
                principalColumn: "booking_passenger_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_booking_passengers_trip_seats_trip_seat_id",
                table: "booking_passengers",
                column: "trip_seat_id",
                principalTable: "trip_seats",
                principalColumn: "trip_seat_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_booking_passengers_trip_seats_trip_seat_id",
                table: "booking_passengers");

            migrationBuilder.DropForeignKey(
                name: "FK_tickets_booking_passengers_booking_passenger_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_booking_passenger_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "booking_passenger_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_booking_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_booking_passengers_trip_seat_id",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "trip_seat_id",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "unit_price",
                table: "booking_passengers");

            migrationBuilder.AddColumn<Guid>(
                name: "ticket_item_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ticket_types",
                columns: table => new
                {
                    ticket_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allowed_seat_type_codes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    price_modifier = table.Column<decimal>(type: "numeric(5,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_types", x => x.ticket_type_id);
                });

            migrationBuilder.CreateTable(
                name: "ticket_items",
                columns: table => new
                {
                    ticket_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_passenger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trip_seat_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_items", x => x.ticket_item_id);
                    table.ForeignKey(
                        name: "FK_ticket_items_booking_passengers_booking_passenger_id",
                        column: x => x.booking_passenger_id,
                        principalTable: "booking_passengers",
                        principalColumn: "booking_passenger_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ticket_items_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "booking_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ticket_items_ticket_types_ticket_type_id",
                        column: x => x.ticket_type_id,
                        principalTable: "ticket_types",
                        principalColumn: "ticket_type_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ticket_items_trip_seats_trip_seat_id",
                        column: x => x.trip_seat_id,
                        principalTable: "trip_seats",
                        principalColumn: "trip_seat_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_booking_id",
                table: "tickets",
                column: "booking_id",
                unique: true,
                filter: "\"booking_passenger_id\" IS NULL AND \"ticket_item_id\" IS NULL AND \"status\" NOT IN ('Cancelled', 'Expired')");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_ticket_item_id",
                table: "tickets",
                column: "ticket_item_id",
                unique: true,
                filter: "\"ticket_item_id\" IS NOT NULL AND \"status\" NOT IN ('Cancelled', 'Expired')");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_items_booking_id",
                table: "ticket_items",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_items_booking_passenger_id",
                table: "ticket_items",
                column: "booking_passenger_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_items_ticket_type_id",
                table: "ticket_items",
                column: "ticket_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_items_trip_seat_id",
                table: "ticket_items",
                column: "trip_seat_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_types_code",
                table: "ticket_types",
                column: "code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_tickets_ticket_items_ticket_item_id",
                table: "tickets",
                column: "ticket_item_id",
                principalTable: "ticket_items",
                principalColumn: "ticket_item_id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
