using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomBookingRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "custom_booking_requests",
                columns: table => new
                {
                    custom_booking_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contact_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    departure_date = table.Column<DateOnly>(type: "date", nullable: false),
                    preferred_start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    preferred_end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    from_location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    to_location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    from_station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_station_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    to_station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_station_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    itinerary_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    passenger_count = table.Column<int>(type: "integer", nullable: false),
                    special_requests = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    quoted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    quoted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quote_accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_booking_requests", x => x.custom_booking_request_id);
                    table.ForeignKey(
                        name: "FK_custom_booking_requests_stations_from_station_id",
                        column: x => x.from_station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_custom_booking_requests_stations_to_station_id",
                        column: x => x.to_station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_custom_booking_requests_users_contact_user_id",
                        column: x => x.contact_user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_custom_booking_requests_users_quoted_by_user_id",
                        column: x => x.quoted_by_user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_custom_booking_requests_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "custom_booking_quotes",
                columns: table => new
                {
                    custom_booking_quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    custom_booking_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quoted_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    deposit_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    remaining_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    price_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_booking_quotes", x => x.custom_booking_quote_id);
                    table.ForeignKey(
                        name: "FK_custom_booking_quotes_custom_booking_requests_custom_bookin~",
                        column: x => x.custom_booking_request_id,
                        principalTable: "custom_booking_requests",
                        principalColumn: "custom_booking_request_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_quotes_custom_booking_request_id",
                table: "custom_booking_quotes",
                column: "custom_booking_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_requests_contact_user_id",
                table: "custom_booking_requests",
                column: "contact_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_requests_from_station_id",
                table: "custom_booking_requests",
                column: "from_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_requests_quoted_by_user_id",
                table: "custom_booking_requests",
                column: "quoted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_requests_status_departure_date",
                table: "custom_booking_requests",
                columns: new[] { "status", "departure_date" });

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_requests_to_station_id",
                table: "custom_booking_requests",
                column: "to_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_requests_user_id",
                table: "custom_booking_requests",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_booking_quotes");

            migrationBuilder.DropTable(
                name: "custom_booking_requests");
        }
    }
}
