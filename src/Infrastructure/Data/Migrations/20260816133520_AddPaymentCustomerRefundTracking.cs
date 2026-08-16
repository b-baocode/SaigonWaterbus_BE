using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentCustomerRefundTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomerRefundAttempts",
                table: "payments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RefundReleasedAt",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RefundReleasedByUserId",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundReleasedReason",
                table: "payments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_RefundReleasedAt",
                table: "payments",
                column: "RefundReleasedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_RefundReleasedAt",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "RefundReleasedReason",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "RefundReleasedByUserId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "RefundReleasedAt",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CustomerRefundAttempts",
                table: "payments");
        }
    }
}
