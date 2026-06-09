using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBoatSeat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_booking_items_seats_seat_id",
                table: "booking_items");

            migrationBuilder.DropForeignKey(
                name: "FK_trips_boats_boat_id",
                table: "trips");

            migrationBuilder.DropTable(
                name: "seat_holds");

            migrationBuilder.DropTable(
                name: "seats");

            migrationBuilder.DropTable(
                name: "boats");

            migrationBuilder.DropIndex(
                name: "IX_trips_boat_id_operating_date",
                table: "trips");

            migrationBuilder.DropIndex(
                name: "IX_booking_items_seat_id",
                table: "booking_items");

            migrationBuilder.DropIndex(
                name: "IX_booking_items_trip_id_seat_id",
                table: "booking_items");

            migrationBuilder.DropColumn(
                name: "boat_id",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "seat_id",
                table: "booking_items");

            migrationBuilder.CreateIndex(
                name: "IX_booking_items_trip_id",
                table: "booking_items",
                column: "trip_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_booking_items_trip_id",
                table: "booking_items");

            migrationBuilder.AddColumn<Guid>(
                name: "boat_id",
                table: "trips",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "seat_id",
                table: "booking_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "boats",
                columns: table => new
                {
                    boat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boat_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    boat_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    boat_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_boats", x => x.boat_id);
                });

            migrationBuilder.CreateTable(
                name: "seats",
                columns: table => new
                {
                    seat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    seat_class = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    seat_column = table.Column<int>(type: "integer", nullable: true),
                    seat_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    seat_row = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seats", x => x.seat_id);
                    table.ForeignKey(
                        name: "FK_seats_boats_boat_id",
                        column: x => x.boat_id,
                        principalTable: "boats",
                        principalColumn: "boat_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seat_holds",
                columns: table => new
                {
                    seat_hold_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    held_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    hold_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seat_holds", x => x.seat_hold_id);
                    table.ForeignKey(
                        name: "FK_seat_holds_seats_seat_id",
                        column: x => x.seat_id,
                        principalTable: "seats",
                        principalColumn: "seat_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_seat_holds_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_seat_holds_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trips_boat_id_operating_date",
                table: "trips",
                columns: new[] { "boat_id", "operating_date" });

            migrationBuilder.CreateIndex(
                name: "IX_booking_items_seat_id",
                table: "booking_items",
                column: "seat_id");

            migrationBuilder.CreateIndex(
                name: "IX_booking_items_trip_id_seat_id",
                table: "booking_items",
                columns: new[] { "trip_id", "seat_id" },
                unique: true,
                filter: "seat_id IS NOT NULL AND item_status != 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_boats_boat_code",
                table: "boats",
                column: "boat_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seat_holds_seat_id",
                table: "seat_holds",
                column: "seat_id");

            migrationBuilder.CreateIndex(
                name: "IX_seat_holds_trip_id_seat_id",
                table: "seat_holds",
                columns: new[] { "trip_id", "seat_id" },
                unique: true,
                filter: "hold_status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_seat_holds_user_id",
                table: "seat_holds",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_seats_boat_id_seat_number",
                table: "seats",
                columns: new[] { "boat_id", "seat_number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_booking_items_seats_seat_id",
                table: "booking_items",
                column: "seat_id",
                principalTable: "seats",
                principalColumn: "seat_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_trips_boats_boat_id",
                table: "trips",
                column: "boat_id",
                principalTable: "boats",
                principalColumn: "boat_id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
