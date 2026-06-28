using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(global::SaigonWaterbus.Infrastructure.Data.ApplicationDbContext))]
    [Migration("20260628093000_AddIncidents")]
    public partial class AddIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incidents",
                columns: table => new
                {
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reported_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    incident_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    severity = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolution_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    assigned_manager_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replacement_boat_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replacement_assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replacement_assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replacement_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidents", x => x.incident_id);
                    table.ForeignKey(
                        name: "FK_incidents_boats_boat_id",
                        column: x => x.boat_id,
                        principalTable: "boats",
                        principalColumn: "boat_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_incidents_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_incidents_users_reported_by_user_id",
                        column: x => x.reported_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_incidents_users_assigned_manager_id",
                        column: x => x.assigned_manager_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_incidents_users_assigned_by_user_id",
                        column: x => x.assigned_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_incidents_boats_replacement_boat_id",
                        column: x => x.replacement_boat_id,
                        principalTable: "boats",
                        principalColumn: "boat_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_incidents_users_replacement_assigned_by_user_id",
                        column: x => x.replacement_assigned_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_incidents_users_resolved_by_user_id",
                        column: x => x.resolved_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_boat_id_resolution_status",
                table: "incidents",
                columns: new[] { "boat_id", "resolution_status" });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_assigned_by_user_id",
                table: "incidents",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_assigned_manager_id",
                table: "incidents",
                column: "assigned_manager_id");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_reported_by_user_id",
                table: "incidents",
                column: "reported_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_replacement_assigned_by_user_id",
                table: "incidents",
                column: "replacement_assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_replacement_boat_id",
                table: "incidents",
                column: "replacement_boat_id");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_resolved_by_user_id",
                table: "incidents",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_trip_id",
                table: "incidents",
                column: "trip_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incidents");
        }
    }
}
