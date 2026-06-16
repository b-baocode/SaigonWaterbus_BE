using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomBookingOperationAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assigned_manager_user_id",
                table: "custom_booking_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "manager_assigned_at",
                table: "custom_booking_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "manager_assigned_by_user_id",
                table: "custom_booking_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "custom_booking_operation_services",
                columns: table => new
                {
                    custom_booking_operation_service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    custom_booking_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_booking_operation_services", x => x.custom_booking_operation_service_id);
                    table.ForeignKey(
                        name: "FK_custom_booking_operation_services_custom_booking_requests_c~",
                        column: x => x.custom_booking_request_id,
                        principalTable: "custom_booking_requests",
                        principalColumn: "custom_booking_request_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "custom_booking_staff_assignments",
                columns: table => new
                {
                    custom_booking_staff_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    custom_booking_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    duty_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_by_manager_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_booking_staff_assignments", x => x.custom_booking_staff_assignment_id);
                    table.ForeignKey(
                        name: "FK_custom_booking_staff_assignments_custom_booking_requests_cu~",
                        column: x => x.custom_booking_request_id,
                        principalTable: "custom_booking_requests",
                        principalColumn: "custom_booking_request_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_custom_booking_staff_assignments_users_assigned_by_manager_~",
                        column: x => x.assigned_by_manager_user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_custom_booking_staff_assignments_users_staff_user_id",
                        column: x => x.staff_user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_requests_assigned_manager_user_id",
                table: "custom_booking_requests",
                column: "assigned_manager_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_requests_manager_assigned_by_user_id",
                table: "custom_booking_requests",
                column: "manager_assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_operation_services_custom_booking_request_id",
                table: "custom_booking_operation_services",
                column: "custom_booking_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_staff_assignments_assigned_by_manager_user_id",
                table: "custom_booking_staff_assignments",
                column: "assigned_by_manager_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_staff_assignments_custom_booking_request_id_~",
                table: "custom_booking_staff_assignments",
                columns: new[] { "custom_booking_request_id", "staff_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_staff_assignments_staff_user_id",
                table: "custom_booking_staff_assignments",
                column: "staff_user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_custom_booking_requests_users_assigned_manager_user_id",
                table: "custom_booking_requests",
                column: "assigned_manager_user_id",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_custom_booking_requests_users_manager_assigned_by_user_id",
                table: "custom_booking_requests",
                column: "manager_assigned_by_user_id",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_custom_booking_requests_users_assigned_manager_user_id",
                table: "custom_booking_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_custom_booking_requests_users_manager_assigned_by_user_id",
                table: "custom_booking_requests");

            migrationBuilder.DropTable(
                name: "custom_booking_operation_services");

            migrationBuilder.DropTable(
                name: "custom_booking_staff_assignments");

            migrationBuilder.DropIndex(
                name: "IX_custom_booking_requests_assigned_manager_user_id",
                table: "custom_booking_requests");

            migrationBuilder.DropIndex(
                name: "IX_custom_booking_requests_manager_assigned_by_user_id",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "assigned_manager_user_id",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "manager_assigned_at",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "manager_assigned_by_user_id",
                table: "custom_booking_requests");
        }
    }
}
