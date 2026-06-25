using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.Sql("""
                CREATE SEQUENCE IF NOT EXISTS user_code_ad_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;
                CREATE SEQUENCE IF NOT EXISTS user_code_mg_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;
                CREATE SEQUENCE IF NOT EXISTS user_code_cu_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;
                CREATE SEQUENCE IF NOT EXISTS user_code_st_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;
                """);

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
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotions", x => x.promotion_id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    role_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.role_id);
                });

            migrationBuilder.CreateTable(
                name: "routes",
                columns: table => new
                {
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    route_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    base_distance_km = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    estimated_duration_min = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routes", x => x.route_id);
                });

            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    service_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    booking_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_services", x => x.service_id);
                });

            migrationBuilder.CreateTable(
                name: "stations",
                columns: table => new
                {
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    station_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    image_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    has_waiting_area = table.Column<bool>(type: "boolean", nullable: false),
                    has_parking = table.Column<bool>(type: "boolean", nullable: false),
                    has_ticket_counter = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    price_modifier = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_types", x => x.ticket_type_id);
                });

            migrationBuilder.CreateTable(
                name: "vessels",
                columns: table => new
                {
                    vessel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vessel_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    registration_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    vessel_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    seat_count = table.Column<int>(type: "integer", nullable: false),
                    number_of_decks = table.Column<int>(type: "integer", nullable: false),
                    seat_setup_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    image_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    hourly_rental_price = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    daily_rental_price = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vessels", x => x.vessel_id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    phone_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    avatar_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_users_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "role_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "landmarks",
                columns: table => new
                {
                    landmark_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    landmark_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    audio_vi_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    audio_en_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                name: "fare_rules",
                columns: table => new
                {
                    fare_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_period = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    base_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fare_rules", x => x.fare_rule_id);
                    table.ForeignKey(
                        name: "FK_fare_rules_routes_route_id",
                        column: x => x.route_id,
                        principalTable: "routes",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fare_rules_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "service_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fare_rules_stations_from_station_id",
                        column: x => x.from_station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fare_rules_stations_to_station_id",
                        column: x => x.to_station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fare_rules_ticket_types_ticket_type_id",
                        column: x => x.ticket_type_id,
                        principalTable: "ticket_types",
                        principalColumn: "ticket_type_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trips",
                columns: table => new
                {
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vessel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trip_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    operating_date = table.Column<DateOnly>(type: "date", nullable: false),
                    service_period = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    departure_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    arrival_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trips", x => x.trip_id);
                    table.ForeignKey(
                        name: "FK_trips_routes_route_id",
                        column: x => x.route_id,
                        principalTable: "routes",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trips_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "service_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trips_vessels_vessel_id",
                        column: x => x.vessel_id,
                        principalTable: "vessels",
                        principalColumn: "vessel_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "vessel_seats",
                columns: table => new
                {
                    seat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vessel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    seat_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    deck_number = table.Column<int>(type: "integer", nullable: false),
                    seat_row = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    seat_column = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vessel_seats", x => x.seat_id);
                    table.ForeignKey(
                        name: "FK_vessel_seats_vessels_vessel_id",
                        column: x => x.vessel_id,
                        principalTable: "vessels",
                        principalColumn: "vessel_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "blog_posts",
                columns: table => new
                {
                    blog_post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    content = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blog_posts", x => x.blog_post_id);
                    table.ForeignKey(
                        name: "FK_blog_posts_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "otp_challenges",
                columns: table => new
                {
                    otp_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    destination = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    pending_value = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    code_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resend_available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_otp_challenges", x => x.otp_challenge_id);
                    table.ForeignKey(
                        name: "FK_otp_challenges_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    booking_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    contact_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    payment_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    deposit_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    remaining_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    booking_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                        name: "FK_bookings_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "service_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bookings_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_bookings_users_customer_user_id",
                        column: x => x.customer_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "booking_passengers",
                columns: table => new
                {
                    booking_passenger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    phone_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    passenger_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    identity_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_booking_passengers", x => x.booking_passenger_id);
                    table.ForeignKey(
                        name: "FK_booking_passengers_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "booking_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    provider_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.payment_id);
                    table.ForeignKey(
                        name: "FK_payments_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "booking_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                columns: table => new
                {
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reviews", x => x.review_id);
                    table.ForeignKey(
                        name: "FK_reviews_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "booking_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_reviews_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_reviews_users_customer_user_id",
                        column: x => x.customer_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_blog_posts_author_user_id",
                table: "blog_posts",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_blog_posts_slug",
                table: "blog_posts",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_booking_passengers_booking_id",
                table: "booking_passengers",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_booking_code",
                table: "bookings",
                column: "booking_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bookings_customer_user_id",
                table: "bookings",
                column: "customer_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_promotion_id",
                table: "bookings",
                column: "promotion_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_service_id",
                table: "bookings",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_trip_id",
                table: "bookings",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "IX_fare_rules_from_station_id",
                table: "fare_rules",
                column: "from_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_fare_rules_route_id_from_station_id_to_station_id",
                table: "fare_rules",
                columns: new[] { "route_id", "from_station_id", "to_station_id" },
                unique: true,
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "IX_fare_rules_service_id",
                table: "fare_rules",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "IX_fare_rules_ticket_type_id",
                table: "fare_rules",
                column: "ticket_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_fare_rules_to_station_id",
                table: "fare_rules",
                column: "to_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_landmarks_station_id",
                table: "landmarks",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "IX_otp_challenges_expires_at",
                table: "otp_challenges",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_otp_challenges_user_id_purpose_consumed_at",
                table: "otp_challenges",
                columns: new[] { "user_id", "purpose", "consumed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_booking_id",
                table: "payments",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "IX_payments_payment_code",
                table: "payments",
                column: "payment_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_promotions_promotion_code",
                table: "promotions",
                column: "promotion_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reviews_booking_id",
                table: "reviews",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "IX_reviews_customer_user_id",
                table: "reviews",
                column: "customer_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_reviews_trip_id",
                table: "reviews",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "IX_roles_role_code",
                table: "roles",
                column: "role_code",
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
                name: "IX_services_display_order",
                table: "services",
                column: "display_order");

            migrationBuilder.CreateIndex(
                name: "IX_services_service_code",
                table: "services",
                column: "service_code",
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
                name: "IX_trips_route_id_operating_date",
                table: "trips",
                columns: new[] { "route_id", "operating_date" });

            migrationBuilder.CreateIndex(
                name: "IX_trips_service_id",
                table: "trips",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "IX_trips_trip_code",
                table: "trips",
                column: "trip_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trips_vessel_id",
                table: "trips",
                column: "vessel_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true,
                filter: "\"email\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_phone_number",
                table: "users",
                column: "phone_number",
                unique: true,
                filter: "\"phone_number\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_role_id",
                table: "users",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_user_code",
                table: "users",
                column: "user_code",
                unique: true,
                filter: "\"user_code\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_vessel_seats_vessel_id_deck_number_seat_row_seat_column",
                table: "vessel_seats",
                columns: new[] { "vessel_id", "deck_number", "seat_row", "seat_column" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vessel_seats_vessel_id_seat_code",
                table: "vessel_seats",
                columns: new[] { "vessel_id", "seat_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vessels_registration_number",
                table: "vessels",
                column: "registration_number",
                unique: true,
                filter: "\"registration_number\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_vessels_status",
                table: "vessels",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_vessels_vessel_code",
                table: "vessels",
                column: "vessel_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blog_posts");

            migrationBuilder.DropTable(
                name: "booking_passengers");

            migrationBuilder.DropTable(
                name: "fare_rules");

            migrationBuilder.DropTable(
                name: "landmarks");

            migrationBuilder.DropTable(
                name: "otp_challenges");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "reviews");

            migrationBuilder.DropTable(
                name: "route_stops");

            migrationBuilder.DropTable(
                name: "vessel_seats");

            migrationBuilder.DropTable(
                name: "ticket_types");

            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "stations");

            migrationBuilder.DropTable(
                name: "promotions");

            migrationBuilder.DropTable(
                name: "trips");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "routes");

            migrationBuilder.DropTable(
                name: "services");

            migrationBuilder.DropTable(
                name: "vessels");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.Sql("""
                DROP SEQUENCE IF EXISTS user_code_ad_seq;
                DROP SEQUENCE IF EXISTS user_code_mg_seq;
                DROP SEQUENCE IF EXISTS user_code_cu_seq;
                DROP SEQUENCE IF EXISTS user_code_st_seq;
                """);
        }
    }
}
