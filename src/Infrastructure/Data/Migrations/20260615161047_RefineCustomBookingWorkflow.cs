using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RefineCustomBookingWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_custom_booking_requests_vessels_preferred_vessel_id",
                table: "custom_booking_requests");

            migrationBuilder.RenameColumn(
                name: "preferred_vessel_id",
                table: "custom_booking_requests",
                newName: "assigned_vessel_id");

            migrationBuilder.RenameIndex(
                name: "IX_custom_booking_requests_preferred_vessel_id",
                table: "custom_booking_requests",
                newName: "IX_custom_booking_requests_assigned_vessel_id");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "assigned_at",
                table: "custom_booking_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_by_user_id",
                table: "custom_booking_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                table: "custom_booking_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelled_by_user_id",
                table: "custom_booking_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "requested_number_of_decks",
                table: "custom_booking_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "requested_seat_setup_type",
                table: "custom_booking_requests",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status_reason",
                table: "custom_booking_requests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE custom_booking_requests AS request
                SET requested_number_of_decks = vessel."NumberOfDecks",
                    requested_seat_setup_type = vessel."SeatSetupType",
                    assigned_at = COALESCE(request.assigned_at, request.updated_at)
                FROM vessels AS vessel
                WHERE request.assigned_vessel_id = vessel."Id";

                UPDATE custom_booking_requests
                SET requested_number_of_decks = COALESCE(requested_number_of_decks, 1),
                    requested_seat_setup_type = COALESCE(requested_seat_setup_type, 'FullStandard');

                UPDATE custom_booking_requests
                SET status = 'Confirmed'
                WHERE status = 'QuoteAccepted';

                UPDATE custom_booking_requests
                SET status = 'Cancelled',
                    status_reason = COALESCE(status_reason, 'Khách đã từ chối báo giá theo dữ liệu cũ.'),
                    cancelled_at = COALESCE(cancelled_at, updated_at)
                WHERE status = 'QuoteRejected';

                UPDATE custom_booking_requests
                SET status_reason = COALESCE(status_reason, 'Yêu cầu đã hủy trước khi nâng cấp luồng.'),
                    cancelled_at = COALESCE(cancelled_at, updated_at)
                WHERE status = 'Cancelled';
                """);

            migrationBuilder.AlterColumn<int>(
                name: "requested_number_of_decks",
                table: "custom_booking_requests",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "requested_seat_setup_type",
                table: "custom_booking_requests",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_requests_assigned_by_user_id",
                table: "custom_booking_requests",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_requests_cancelled_by_user_id",
                table: "custom_booking_requests",
                column: "cancelled_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_custom_booking_requests_users_assigned_by_user_id",
                table: "custom_booking_requests",
                column: "assigned_by_user_id",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_custom_booking_requests_users_cancelled_by_user_id",
                table: "custom_booking_requests",
                column: "cancelled_by_user_id",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_custom_booking_requests_vessels_assigned_vessel_id",
                table: "custom_booking_requests",
                column: "assigned_vessel_id",
                principalTable: "vessels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_custom_booking_requests_users_assigned_by_user_id",
                table: "custom_booking_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_custom_booking_requests_users_cancelled_by_user_id",
                table: "custom_booking_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_custom_booking_requests_vessels_assigned_vessel_id",
                table: "custom_booking_requests");

            migrationBuilder.DropIndex(
                name: "IX_custom_booking_requests_assigned_by_user_id",
                table: "custom_booking_requests");

            migrationBuilder.DropIndex(
                name: "IX_custom_booking_requests_cancelled_by_user_id",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "assigned_at",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "assigned_by_user_id",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "cancelled_by_user_id",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "requested_number_of_decks",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "requested_seat_setup_type",
                table: "custom_booking_requests");

            migrationBuilder.DropColumn(
                name: "status_reason",
                table: "custom_booking_requests");

            migrationBuilder.RenameColumn(
                name: "assigned_vessel_id",
                table: "custom_booking_requests",
                newName: "preferred_vessel_id");

            migrationBuilder.RenameIndex(
                name: "IX_custom_booking_requests_assigned_vessel_id",
                table: "custom_booking_requests",
                newName: "IX_custom_booking_requests_preferred_vessel_id");

            migrationBuilder.AddForeignKey(
                name: "FK_custom_booking_requests_vessels_preferred_vessel_id",
                table: "custom_booking_requests",
                column: "preferred_vessel_id",
                principalTable: "vessels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
