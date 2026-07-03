using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketItemsPointsNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_booking_passengers_seats_seat_id",
                table: "booking_passengers");

            migrationBuilder.DropForeignKey(
                name: "FK_tickets_booking_passengers_booking_passenger_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_booking_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_booking_passenger_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_booking_passengers_seat_id",
                table: "booking_passengers");

            migrationBuilder.RenameColumn(
                name: "booking_passenger_id",
                table: "tickets",
                newName: "ticket_item_id");

            migrationBuilder.AddColumn<int>(
                name: "point_balance",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "birth_year",
                table: "booking_passengers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gender",
                table: "booking_passengers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nationality",
                table: "booking_passengers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "booking_passengers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    related_entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    related_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.notification_id);
                    table.ForeignKey(
                        name: "FK_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "point_transactions",
                columns: table => new
                {
                    point_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transaction_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    balance_after = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_point_transactions", x => x.point_transaction_id);
                    table.ForeignKey(
                        name: "FK_point_transactions_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "booking_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_point_transactions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_items",
                columns: table => new
                {
                    ticket_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_passenger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    ticket_type_id = table.Column<Guid>(type: "uuid", nullable: true)
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
                        name: "FK_ticket_items_seats_seat_id",
                        column: x => x.seat_id,
                        principalTable: "seats",
                        principalColumn: "seat_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ticket_items_ticket_types_ticket_type_id",
                        column: x => x.ticket_type_id,
                        principalTable: "ticket_types",
                        principalColumn: "ticket_type_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.Sql(
                @"INSERT INTO ticket_items (ticket_item_id, booking_id, booking_passenger_id, seat_id, unit_price, ticket_type_id)
                  SELECT
                      bp.booking_passenger_id,
                      bp.booking_id,
                      bp.booking_passenger_id,
                      bp.seat_id,
                      bp.unit_price,
                      ticket_type.ticket_type_id
                  FROM booking_passengers AS bp
                  LEFT JOIN LATERAL (
                      SELECT t.ticket_type_id
                      FROM tickets AS t
                      WHERE t.ticket_item_id = bp.booking_passenger_id
                      ORDER BY
                          CASE WHEN t.status NOT IN ('Cancelled', 'Expired') THEN 0 ELSE 1 END,
                          t.issued_at DESC
                      LIMIT 1
                  ) AS ticket_type ON TRUE
                  ON CONFLICT (ticket_item_id) DO NOTHING;");

            migrationBuilder.Sql(
                @"UPDATE booking_passengers
                  SET birth_year = EXTRACT(YEAR FROM date_of_birth)::integer
                  WHERE date_of_birth IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "date_of_birth",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "identity_number",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "seat_code",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "seat_id",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "unit_price",
                table: "booking_passengers");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_booking_id",
                table: "tickets",
                column: "booking_id",
                unique: true,
                filter: "\"ticket_item_id\" IS NULL AND \"status\" NOT IN ('Cancelled', 'Expired')");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_ticket_item_id",
                table: "tickets",
                column: "ticket_item_id",
                unique: true,
                filter: "\"ticket_item_id\" IS NOT NULL AND \"status\" NOT IN ('Cancelled', 'Expired')");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_user_id_is_read",
                table: "notifications",
                columns: new[] { "user_id", "is_read" });

            migrationBuilder.CreateIndex(
                name: "IX_point_transactions_booking_id",
                table: "point_transactions",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "IX_point_transactions_user_id",
                table: "point_transactions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_items_booking_id",
                table: "ticket_items",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_items_booking_passenger_id",
                table: "ticket_items",
                column: "booking_passenger_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_items_seat_id",
                table: "ticket_items",
                column: "seat_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_items_ticket_type_id",
                table: "ticket_items",
                column: "ticket_type_id");

            migrationBuilder.AddForeignKey(
                name: "FK_tickets_ticket_items_ticket_item_id",
                table: "tickets",
                column: "ticket_item_id",
                principalTable: "ticket_items",
                principalColumn: "ticket_item_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tickets_ticket_items_ticket_item_id",
                table: "tickets");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "point_transactions");

            migrationBuilder.DropTable(
                name: "ticket_items");

            migrationBuilder.DropIndex(
                name: "IX_tickets_booking_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_ticket_item_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "point_balance",
                table: "users");

            migrationBuilder.DropColumn(
                name: "birth_year",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "gender",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "nationality",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "note",
                table: "booking_passengers");

            migrationBuilder.RenameColumn(
                name: "ticket_item_id",
                table: "tickets",
                newName: "booking_passenger_id");

            migrationBuilder.AddColumn<DateOnly>(
                name: "date_of_birth",
                table: "booking_passengers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "identity_number",
                table: "booking_passengers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seat_code",
                table: "booking_passengers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "seat_id",
                table: "booking_passengers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_price",
                table: "booking_passengers",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

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
                name: "IX_booking_passengers_seat_id",
                table: "booking_passengers",
                column: "seat_id");

            migrationBuilder.AddForeignKey(
                name: "FK_booking_passengers_seats_seat_id",
                table: "booking_passengers",
                column: "seat_id",
                principalTable: "seats",
                principalColumn: "seat_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_tickets_booking_passengers_booking_passenger_id",
                table: "tickets",
                column: "booking_passenger_id",
                principalTable: "booking_passengers",
                principalColumn: "booking_passenger_id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
