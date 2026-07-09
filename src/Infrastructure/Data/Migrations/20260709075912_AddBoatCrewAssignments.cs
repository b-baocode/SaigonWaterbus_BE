using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBoatCrewAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "boat_crew_assignments",
                columns: table => new
                {
                    boat_crew_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    crew_role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: true),
                    replaces_assignment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replacement_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "boat_crew_assignments");
        }
    }
}
