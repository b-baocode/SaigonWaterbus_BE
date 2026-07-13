using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceBoatCrewAssignmentsWithStaffWorkAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_work_assignments",
                columns: table => new
                {
                    staff_work_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    boat_id = table.Column<Guid>(type: "uuid", nullable: true),
                    station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    working_date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duty_role = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_work_assignments", x => x.staff_work_assignment_id);
                    table.ForeignKey(
                        name: "FK_staff_work_assignments_boats_boat_id",
                        column: x => x.boat_id,
                        principalTable: "boats",
                        principalColumn: "boat_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_staff_work_assignments_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_staff_work_assignments_users_assigned_by_user_id",
                        column: x => x.assigned_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_staff_work_assignments_users_staff_user_id",
                        column: x => x.staff_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_staff_work_assignments_assigned_by_user_id",
                table: "staff_work_assignments",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_staff_work_assignments_assignment_type_boat_id_status",
                table: "staff_work_assignments",
                columns: new[] { "assignment_type", "boat_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_staff_work_assignments_assignment_type_station_id_status",
                table: "staff_work_assignments",
                columns: new[] { "assignment_type", "station_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_staff_work_assignments_boat_id",
                table: "staff_work_assignments",
                column: "boat_id");

            migrationBuilder.CreateIndex(
                name: "IX_staff_work_assignments_staff_user_id_working_date_status",
                table: "staff_work_assignments",
                columns: new[] { "staff_user_id", "working_date", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_staff_work_assignments_station_id",
                table: "staff_work_assignments",
                column: "station_id");

            migrationBuilder.Sql(
                """
                INSERT INTO staff_work_assignments (
                    staff_work_assignment_id,
                    staff_user_id,
                    assignment_type,
                    boat_id,
                    station_id,
                    working_date,
                    start_at,
                    end_at,
                    duty_role,
                    status,
                    assigned_by_user_id,
                    assigned_at,
                    note,
                    created_at,
                    updated_at
                )
                SELECT
                    boat_crew_assignment_id,
                    staff_user_id,
                    'Boat',
                    boat_id,
                    NULL,
                    from_date,
                    from_date::timestamp AT TIME ZONE 'Asia/Ho_Chi_Minh',
                    ((COALESCE(to_date, DATE '2099-12-31') + 1)::timestamp AT TIME ZONE 'Asia/Ho_Chi_Minh'),
                    crew_role,
                    CASE WHEN is_active THEN 'Scheduled' ELSE 'Cancelled' END,
                    assigned_by_user_id,
                    assigned_at,
                    replacement_reason,
                    created_at,
                    updated_at
                FROM boat_crew_assignments;
                """);

            migrationBuilder.DropTable(
                name: "boat_crew_assignments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "boat_crew_assignments",
                columns: table => new
                {
                    boat_crew_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    replaces_assignment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    staff_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    crew_role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    replacement_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    to_date = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_boat_crew_assignments", x => x.boat_crew_assignment_id);
                    table.ForeignKey(
                        name: "FK_boat_crew_assignments_boat_crew_assignments_replaces_assign~",
                        column: x => x.replaces_assignment_id,
                        principalTable: "boat_crew_assignments",
                        principalColumn: "boat_crew_assignment_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_boat_crew_assignments_boats_boat_id",
                        column: x => x.boat_id,
                        principalTable: "boats",
                        principalColumn: "boat_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_boat_crew_assignments_users_assigned_by_user_id",
                        column: x => x.assigned_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_boat_crew_assignments_users_staff_user_id",
                        column: x => x.staff_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_boat_crew_assignments_assigned_by_user_id",
                table: "boat_crew_assignments",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_boat_crew_assignments_boat_id_crew_role_is_active",
                table: "boat_crew_assignments",
                columns: new[] { "boat_id", "crew_role", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_boat_crew_assignments_replaces_assignment_id",
                table: "boat_crew_assignments",
                column: "replaces_assignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_boat_crew_assignments_staff_user_id_is_active",
                table: "boat_crew_assignments",
                columns: new[] { "staff_user_id", "is_active" });

            migrationBuilder.Sql(
                """
                INSERT INTO boat_crew_assignments (
                    boat_crew_assignment_id,
                    assigned_by_user_id,
                    boat_id,
                    replaces_assignment_id,
                    staff_user_id,
                    assigned_at,
                    created_at,
                    crew_role,
                    from_date,
                    is_active,
                    updated_at,
                    replacement_reason,
                    to_date
                )
                SELECT
                    staff_work_assignment_id,
                    assigned_by_user_id,
                    boat_id,
                    NULL,
                    staff_user_id,
                    assigned_at,
                    created_at,
                    CASE
                        WHEN duty_role IN ('OnBoard', 'Captain', 'Deckhand') THEN duty_role
                        ELSE 'OnBoard'
                    END,
                    working_date,
                    status <> 'Cancelled',
                    updated_at,
                    note,
                    ((end_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date - 1)
                FROM staff_work_assignments
                WHERE assignment_type = 'Boat'
                  AND boat_id IS NOT NULL;
                """);

            migrationBuilder.DropTable(
                name: "staff_work_assignments");
        }
    }
}
