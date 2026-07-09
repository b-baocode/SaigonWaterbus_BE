using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffTypeAndStationStaffAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "staff_type",
                table: "users",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "station_staff_assignments");

            migrationBuilder.DropColumn(
                name: "staff_type",
                table: "users");
        }
    }
}
