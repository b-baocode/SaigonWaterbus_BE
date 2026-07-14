using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPassengerAddRequestApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approval_status",
                table: "booking_passengers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.AddColumn<Guid>(
                name: "request_batch_id",
                table: "booking_passengers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "requested_at",
                table: "booking_passengers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "requested_by_user_id",
                table: "booking_passengers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_note",
                table: "booking_passengers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at",
                table: "booking_passengers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by_user_id",
                table: "booking_passengers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_booking_passengers_requested_by_user_id",
                table: "booking_passengers",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_booking_passengers_reviewed_by_user_id",
                table: "booking_passengers",
                column: "reviewed_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_booking_passengers_users_requested_by_user_id",
                table: "booking_passengers",
                column: "requested_by_user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_booking_passengers_users_reviewed_by_user_id",
                table: "booking_passengers",
                column: "reviewed_by_user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_booking_passengers_users_requested_by_user_id",
                table: "booking_passengers");

            migrationBuilder.DropForeignKey(
                name: "FK_booking_passengers_users_reviewed_by_user_id",
                table: "booking_passengers");

            migrationBuilder.DropIndex(
                name: "IX_booking_passengers_requested_by_user_id",
                table: "booking_passengers");

            migrationBuilder.DropIndex(
                name: "IX_booking_passengers_reviewed_by_user_id",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "approval_status",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "request_batch_id",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "requested_at",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "requested_by_user_id",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "review_note",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "booking_passengers");

            migrationBuilder.DropColumn(
                name: "reviewed_by_user_id",
                table: "booking_passengers");
        }
    }
}
