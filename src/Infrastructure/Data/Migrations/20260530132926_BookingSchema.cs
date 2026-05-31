using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class BookingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "boats",
                columns: table => new
                {
                    boat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boat_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    boat_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    boat_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_boats", x => x.boat_id);
                });

            migrationBuilder.CreateTable(
                name: "promotions",
                columns: table => new
                {
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    promotion_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    promotion_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    promotion_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    discount_value = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    min_order_value = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usage_limit = table.Column<int>(type: "integer", nullable: true),
                    usage_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotions", x => x.promotion_id);
                });

            migrationBuilder.CreateTable(
                name: "routes",
                columns: table => new
                {
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    route_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    base_distance_km = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    estimated_duration_min = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routes", x => x.route_id);
                });

            migrationBuilder.CreateTable(
                name: "stations",
                columns: table => new
                {
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    station_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    address = table.Column<string>(type: "text", nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stations", x => x.station_id);
                });

            migrationBuilder.CreateTable(
                name: "ticket_types",
                columns: table => new
                {
                    ticket_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_type_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ticket_type_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    price_modifier = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    points_earned_rate = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_types", x => x.ticket_type_id);
                });

            migrationBuilder.CreateTable(
                name: "seats",
                columns: table => new
                {
                    seat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    seat_class = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    seat_row = table.Column<int>(type: "integer", nullable: true),
                    seat_column = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
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
                name: "bookings",
                columns: table => new
                {
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    booking_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    booked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    booking_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    points_used = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    points_earned = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bookings", x => x.booking_id);
                    table.ForeignKey(
                        name: "FK_bookings_promotions_promotion_id",
                        column: x => x.promotion_id,
                        principalTable: "promotions",
                        principalColumn: "promotion_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_bookings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trips",
                columns: table => new
                {
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    operating_date = table.Column<DateOnly>(type: "date", nullable: false),
                    departure_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    arrival_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    capacity_snapshot = table.Column<int>(type: "integer", nullable: false),
                    trip_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trips", x => x.trip_id);
                    table.ForeignKey(
                        name: "FK_trips_boats_boat_id",
                        column: x => x.boat_id,
                        principalTable: "boats",
                        principalColumn: "boat_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trips_routes_route_id",
                        column: x => x.route_id,
                        principalTable: "routes",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fare_matrices",
                columns: table => new
                {
                    fare_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fare_matrices", x => x.fare_id);
                    table.ForeignKey(
                        name: "FK_fare_matrices_routes_route_id",
                        column: x => x.route_id,
                        principalTable: "routes",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fare_matrices_stations_from_station_id",
                        column: x => x.from_station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fare_matrices_stations_to_station_id",
                        column: x => x.to_station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "landmarks",
                columns: table => new
                {
                    landmark_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    landmark_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    audio_vi_url = table.Column<string>(type: "text", nullable: true),
                    audio_en_url = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_landmarks", x => x.landmark_id);
                    table.ForeignKey(
                        name: "FK_landmarks_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "route_stops",
                columns: table => new
                {
                    route_stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stop_order = table.Column<int>(type: "integer", nullable: false),
                    standard_travel_min = table.Column<int>(type: "integer", nullable: true),
                    standard_dwell_min = table.Column<int>(type: "integer", nullable: true),
                    is_pickup_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    is_dropoff_allowed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_route_stops", x => x.route_stop_id);
                    table.ForeignKey(
                        name: "FK_route_stops_routes_route_id",
                        column: x => x.route_id,
                        principalTable: "routes",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_route_stops_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "incidents",
                columns: table => new
                {
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_by = table.Column<int>(type: "integer", nullable: true),
                    incident_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolution_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidents", x => x.incident_id);
                    table.ForeignKey(
                        name: "FK_incidents_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_incidents_users_reported_by",
                        column: x => x.reported_by,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "seat_holds",
                columns: table => new
                {
                    seat_hold_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    held_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "trip_stops",
                columns: table => new
                {
                    trip_stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stop_order = table.Column<int>(type: "integer", nullable: false),
                    scheduled_arrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scheduled_departure = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actual_arrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actual_departure = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    stop_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trip_stops", x => x.trip_stop_id);
                    table.ForeignKey(
                        name: "FK_trip_stops_route_stops_route_stop_id",
                        column: x => x.route_stop_id,
                        principalTable: "route_stops",
                        principalColumn: "route_stop_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trip_stops_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "booking_items",
                columns: table => new
                {
                    booking_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_trip_stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_trip_stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    passenger_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    passenger_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    item_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_booking_items", x => x.booking_item_id);
                    table.ForeignKey(
                        name: "FK_booking_items_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "booking_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_booking_items_seats_seat_id",
                        column: x => x.seat_id,
                        principalTable: "seats",
                        principalColumn: "seat_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_booking_items_ticket_types_ticket_type_id",
                        column: x => x.ticket_type_id,
                        principalTable: "ticket_types",
                        principalColumn: "ticket_type_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_booking_items_trip_stops_from_trip_stop_id",
                        column: x => x.from_trip_stop_id,
                        principalTable: "trip_stops",
                        principalColumn: "trip_stop_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_booking_items_trip_stops_to_trip_stop_id",
                        column: x => x.to_trip_stop_id,
                        principalTable: "trip_stops",
                        principalColumn: "trip_stop_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_booking_items_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_boats_boat_code",
                table: "boats",
                column: "boat_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_booking_items_booking_id",
                table: "booking_items",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "IX_booking_items_from_trip_stop_id",
                table: "booking_items",
                column: "from_trip_stop_id");

            migrationBuilder.CreateIndex(
                name: "IX_booking_items_seat_id",
                table: "booking_items",
                column: "seat_id");

            migrationBuilder.CreateIndex(
                name: "IX_booking_items_ticket_type_id",
                table: "booking_items",
                column: "ticket_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_booking_items_to_trip_stop_id",
                table: "booking_items",
                column: "to_trip_stop_id");

            migrationBuilder.CreateIndex(
                name: "IX_booking_items_trip_id_seat_id",
                table: "booking_items",
                columns: new[] { "trip_id", "seat_id" },
                unique: true,
                filter: "seat_id IS NOT NULL AND item_status != 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_booking_code",
                table: "bookings",
                column: "booking_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bookings_promotion_id",
                table: "bookings",
                column: "promotion_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_user_id",
                table: "bookings",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_fare_matrices_from_station_id",
                table: "fare_matrices",
                column: "from_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_fare_matrices_route_id_from_station_id_to_station_id",
                table: "fare_matrices",
                columns: new[] { "route_id", "from_station_id", "to_station_id" },
                unique: true,
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "IX_fare_matrices_to_station_id",
                table: "fare_matrices",
                column: "to_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_reported_by",
                table: "incidents",
                column: "reported_by");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_trip_id",
                table: "incidents",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "IX_landmarks_station_id",
                table: "landmarks",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "IX_promotions_promotion_code",
                table: "promotions",
                column: "promotion_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_route_stops_route_id_station_id",
                table: "route_stops",
                columns: new[] { "route_id", "station_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_route_stops_route_id_stop_order",
                table: "route_stops",
                columns: new[] { "route_id", "stop_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_route_stops_station_id",
                table: "route_stops",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "IX_routes_route_code",
                table: "routes",
                column: "route_code",
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

            migrationBuilder.CreateIndex(
                name: "IX_stations_station_code",
                table: "stations",
                column: "station_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ticket_types_ticket_type_code",
                table: "ticket_types",
                column: "ticket_type_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trip_stops_route_stop_id",
                table: "trip_stops",
                column: "route_stop_id");

            migrationBuilder.CreateIndex(
                name: "IX_trip_stops_trip_id_stop_order",
                table: "trip_stops",
                columns: new[] { "trip_id", "stop_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trips_boat_id_operating_date",
                table: "trips",
                columns: new[] { "boat_id", "operating_date" });

            migrationBuilder.CreateIndex(
                name: "IX_trips_route_id_operating_date",
                table: "trips",
                columns: new[] { "route_id", "operating_date" });

            migrationBuilder.CreateIndex(
                name: "IX_trips_trip_code",
                table: "trips",
                column: "trip_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_items");

            migrationBuilder.DropTable(
                name: "fare_matrices");

            migrationBuilder.DropTable(
                name: "incidents");

            migrationBuilder.DropTable(
                name: "landmarks");

            migrationBuilder.DropTable(
                name: "seat_holds");

            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "ticket_types");

            migrationBuilder.DropTable(
                name: "trip_stops");

            migrationBuilder.DropTable(
                name: "seats");

            migrationBuilder.DropTable(
                name: "promotions");

            migrationBuilder.DropTable(
                name: "route_stops");

            migrationBuilder.DropTable(
                name: "trips");

            migrationBuilder.DropTable(
                name: "stations");

            migrationBuilder.DropTable(
                name: "boats");

            migrationBuilder.DropTable(
                name: "routes");
        }
    }
}
