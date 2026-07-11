using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(global::SaigonWaterbus.Infrastructure.Data.ApplicationDbContext))]
    [Migration("20260711090000_RemoveShiftBasedStaffAssignments")]
    public partial class RemoveShiftBasedStaffAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "station_staff_assignments");

            migrationBuilder.DropTable(
                name: "boat_staff_assignments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "boat_staff_assignments",
                columns: table => new
                {
                    boat_staff_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    working_date = table.Column<DateOnly>(type: "date", nullable: false),
                    shift_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    duty_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    replaces_assignment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replaced_by_assignment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replacement_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    replaced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_boat_staff_assignments", x => x.boat_staff_assignment_id);
                    table.ForeignKey(
                        name: "FK_boat_staff_assignments_boats_boat_id",
                        column: x => x.boat_id,
                        principalTable: "boats",
                        principalColumn: "boat_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_boat_staff_assignments_boat_staff_assignments_replaced_by_assignment_id",
                        column: x => x.replaced_by_assignment_id,
                        principalTable: "boat_staff_assignments",
                        principalColumn: "boat_staff_assignment_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_boat_staff_assignments_boat_staff_assignments_replaces_assignment_id",
                        column: x => x.replaces_assignment_id,
                        principalTable: "boat_staff_assignments",
                        principalColumn: "boat_staff_assignment_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_boat_staff_assignments_users_assigned_by_user_id",
                        column: x => x.assigned_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_boat_staff_assignments_users_replaced_by_user_id",
                        column: x => x.replaced_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_boat_staff_assignments_users_staff_user_id",
                        column: x => x.staff_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "station_staff_assignments",
                columns: table => new
                {
                    station_staff_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    working_date = table.Column<DateOnly>(type: "date", nullable: false),
                    shift_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    duty_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_station_staff_assignments", x => x.station_staff_assignment_id);
                    table.ForeignKey(
                        name: "FK_station_staff_assignments_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_station_staff_assignments_users_assigned_by_user_id",
                        column: x => x.assigned_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_station_staff_assignments_users_staff_user_id",
                        column: x => x.staff_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_boat_staff_assignments_assigned_by_user_id",
                table: "boat_staff_assignments",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_boat_staff_assignments_boat_id_working_date_shift_code_is_active",
                table: "boat_staff_assignments",
                columns: new[] { "boat_id", "working_date", "shift_code", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_boat_staff_assignments_replaced_by_assignment_id",
                table: "boat_staff_assignments",
                column: "replaced_by_assignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_boat_staff_assignments_replaced_by_user_id",
                table: "boat_staff_assignments",
                column: "replaced_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_boat_staff_assignments_replaces_assignment_id",
                table: "boat_staff_assignments",
                column: "replaces_assignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_boat_staff_assignments_staff_user_id_working_date_shift_code_is_active",
                table: "boat_staff_assignments",
                columns: new[] { "staff_user_id", "working_date", "shift_code", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_station_staff_assignments_assigned_by_user_id",
                table: "station_staff_assignments",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_station_staff_assignments_source_type_source_id_station_id_~",
                table: "station_staff_assignments",
                columns: new[] { "source_type", "source_id", "station_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_station_staff_assignments_staff_user_id_working_date_shift_~",
                table: "station_staff_assignments",
                columns: new[] { "staff_user_id", "working_date", "shift_code", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_station_staff_assignments_station_id",
                table: "station_staff_assignments",
                column: "station_id");
        }
    }
}
