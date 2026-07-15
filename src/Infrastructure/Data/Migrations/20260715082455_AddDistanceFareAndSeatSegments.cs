using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDistanceFareAndSeatSegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_booking_passengers_trip_seat_id",
                table: "booking_passengers");

            migrationBuilder.AddColumn<decimal>(
                name: "distance_from_previous_km",
                table: "route_stops",
                type: "numeric(8,3)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "from_station_id",
                table: "booking_passengers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "from_stop_order",
                table: "booking_passengers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "to_station_id",
                table: "booking_passengers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "to_stop_order",
                table: "booking_passengers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fare_policies",
                columns: table => new
                {
                    fare_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_fare = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    price_per_km = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    rounding_step = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    min_fare = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fare_policies", x => x.fare_policy_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_booking_passengers_from_station_id",
                table: "booking_passengers",
                column: "from_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_booking_passengers_to_station_id",
                table: "booking_passengers",
                column: "to_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_booking_passengers_trip_seat_id_from_stop_order_to_stop_ord~",
                table: "booking_passengers",
                columns: new[] { "trip_seat_id", "from_stop_order", "to_stop_order" });

            migrationBuilder.AddForeignKey(
                name: "FK_booking_passengers_stations_from_station_id",
                table: "booking_passengers",
                column: "from_station_id",
                principalTable: "stations",
                principalColumn: "station_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_booking_passengers_stations_to_station_id",
                table: "booking_passengers",
                column: "to_station_id",
                principalTable: "stations",
                principalColumn: "station_id",
                onDelete: ReferentialAction.SetNull);

            // Policy mặc định để booking theo km hoạt động ngay sau deploy;
            // admin chỉnh lại qua PUT /api/fare-policy.
            migrationBuilder.InsertData(
                table: "fare_policies",
                columns: new[] { "fare_policy_id", "base_fare", "price_per_km", "rounding_step", "min_fare", "currency", "is_active", "created_at" },
                values: new object[]
                {
                    new Guid("a1f0f7de-0f15-4a10-9a7e-3c5b8d2f6a01"),
                    5000m,
                    1500m,
                    1000m,
                    null,
                    "VND",
                    true,
                    new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero)
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_booking_passengers_stations_from_station_id",
                table: "booking_passengers");

            migrationBuilder.DropForeignKey(
                name: "FK_booking_passengers_stations_to_station_id",
                table: "booking_passengers");

            migrationBuilder.DropTable(
                name: "fare_policies");

            migrationBuilder.DropIndex(
                name: "IX_booking_passengers_from_station_id",
                table: "booking_passengers");

            migrationBuilder.DropIndex(
                name: "IX_booking_passengers_to_station_id",
                table: "booking_passengers");

            migrationBuilder.DropIndex(
                name: "IX_booking_passengers_trip_seat_id_from_stop_order_to_stop_ord~",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "distance_from_previous_km",
                table: "route_stops");

            migrationBuilder.DropColumn(
                name: "from_station_id",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "from_stop_order",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "to_station_id",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "to_stop_order",
                table: "booking_passengers");

            migrationBuilder.CreateIndex(
                name: "IX_booking_passengers_trip_seat_id",
                table: "booking_passengers",
                column: "trip_seat_id");
        }
    }
}
