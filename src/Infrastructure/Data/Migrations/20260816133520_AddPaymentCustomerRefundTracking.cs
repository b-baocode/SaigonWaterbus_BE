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
                name: "customer_refund_attempts",
                table: "payments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refund_released_at",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "refund_released_by_user_id",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_released_reason",
                table: "payments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_refund_released_at",
                table: "payments",
                column: "refund_released_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_refund_released_at",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refund_released_reason",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refund_released_by_user_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refund_released_at",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "customer_refund_attempts",
                table: "payments");
        }
    }
}
