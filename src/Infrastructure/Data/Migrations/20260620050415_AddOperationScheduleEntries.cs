using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationScheduleEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operation_schedule_entries",
                columns: table => new
                {
                    operation_schedule_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    service_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    booking_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    vessel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vessel_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    vessel_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    route_id = table.Column<Guid>(type: "uuid", nullable: true),
                    route_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    route_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    from_station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_station_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    from_location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    to_station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_station_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    to_location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    operating_date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    schedule_state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    payment_stage = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    remaining_payment_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_payment_overdue = table.Column<bool>(type: "boolean", nullable: false),
                    customer_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_manager_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_schedule_entries", x => x.operation_schedule_entry_id);
                    table.ForeignKey(
                        name: "FK_operation_schedule_entries_routes_route_id",
                        column: x => x.route_id,
                        principalTable: "routes",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_operation_schedule_entries_stations_from_station_id",
                        column: x => x.from_station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_operation_schedule_entries_stations_to_station_id",
                        column: x => x.to_station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_operation_schedule_entries_users_assigned_manager_user_id",
                        column: x => x.assigned_manager_user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_operation_schedule_entries_users_customer_user_id",
                        column: x => x.customer_user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_operation_schedule_entries_vessels_vessel_id",
                        column: x => x.vessel_id,
                        principalTable: "vessels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_operation_schedule_entries_waterbus_services_service_id",
                        column: x => x.service_id,
                        principalTable: "waterbus_services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_operation_schedule_entries_assigned_manager_user_id",
                table: "operation_schedule_entries",
                column: "assigned_manager_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_operation_schedule_entries_customer_user_id",
                table: "operation_schedule_entries",
                column: "customer_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_operation_schedule_entries_from_station_id",
                table: "operation_schedule_entries",
                column: "from_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_operation_schedule_entries_operating_date_start_at",
                table: "operation_schedule_entries",
                columns: new[] { "operating_date", "start_at" });

            migrationBuilder.CreateIndex(
                name: "IX_operation_schedule_entries_route_id",
                table: "operation_schedule_entries",
                column: "route_id");

            migrationBuilder.CreateIndex(
                name: "IX_operation_schedule_entries_service_id",
                table: "operation_schedule_entries",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "IX_operation_schedule_entries_source_type_source_id",
                table: "operation_schedule_entries",
                columns: new[] { "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operation_schedule_entries_start_at_end_at",
                table: "operation_schedule_entries",
                columns: new[] { "start_at", "end_at" });

            migrationBuilder.CreateIndex(
                name: "IX_operation_schedule_entries_to_station_id",
                table: "operation_schedule_entries",
                column: "to_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_operation_schedule_entries_vessel_id_start_at",
                table: "operation_schedule_entries",
                columns: new[] { "vessel_id", "start_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operation_schedule_entries");
        }
    }
}
