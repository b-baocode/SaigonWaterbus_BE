using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomBookingDiscountAndRefund : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "discount_code",
                table: "custom_booking_quotes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_amount",
                table: "custom_booking_quotes",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "deposit_payment_status",
                table: "custom_booking_quotes",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "NotCreated");

            migrationBuilder.AddColumn<long>(
                name: "deposit_payment_order_code",
                table: "custom_booking_quotes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deposit_payment_link_id",
                table: "custom_booking_quotes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deposit_payment_checkout_url",
                table: "custom_booking_quotes",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deposit_payment_qr_code",
                table: "custom_booking_quotes",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deposit_payment_created_at",
                table: "custom_booking_quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deposit_payment_paid_at",
                table: "custom_booking_quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deposit_payment_cancelled_at",
                table: "custom_booking_quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deposit_payment_failure_reason",
                table: "custom_booking_quotes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "remaining_payment_status",
                table: "custom_booking_quotes",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "NotCreated");

            migrationBuilder.AddColumn<long>(
                name: "remaining_payment_order_code",
                table: "custom_booking_quotes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "remaining_payment_link_id",
                table: "custom_booking_quotes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "remaining_payment_checkout_url",
                table: "custom_booking_quotes",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "remaining_payment_qr_code",
                table: "custom_booking_quotes",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "remaining_payment_created_at",
                table: "custom_booking_quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "remaining_payment_paid_at",
                table: "custom_booking_quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "remaining_payment_cancelled_at",
                table: "custom_booking_quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "remaining_payment_failure_reason",
                table: "custom_booking_quotes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "refund_eligible_percent",
                table: "custom_booking_quotes",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "refund_amount",
                table: "custom_booking_quotes",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "refund_policy_note",
                table: "custom_booking_quotes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_bank_bin",
                table: "custom_booking_quotes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_account_number",
                table: "custom_booking_quotes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_account_name",
                table: "custom_booking_quotes",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_reference_id",
                table: "custom_booking_quotes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_payout_id",
                table: "custom_booking_quotes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_status",
                table: "custom_booking_quotes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_failure_reason",
                table: "custom_booking_quotes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refund_requested_at",
                table: "custom_booking_quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refund_processed_at",
                table: "custom_booking_quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_quotes_deposit_payment_order_code",
                table: "custom_booking_quotes",
                column: "deposit_payment_order_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_quotes_remaining_payment_order_code",
                table: "custom_booking_quotes",
                column: "remaining_payment_order_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_booking_quotes_refund_reference_id",
                table: "custom_booking_quotes",
                column: "refund_reference_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_custom_booking_quotes_deposit_payment_order_code",
                table: "custom_booking_quotes");

            migrationBuilder.DropIndex(
                name: "IX_custom_booking_quotes_remaining_payment_order_code",
                table: "custom_booking_quotes");

            migrationBuilder.DropIndex(
                name: "IX_custom_booking_quotes_refund_reference_id",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "discount_code",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "discount_amount",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "deposit_payment_status",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "deposit_payment_order_code",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "deposit_payment_link_id",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "deposit_payment_checkout_url",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "deposit_payment_qr_code",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "deposit_payment_created_at",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "deposit_payment_paid_at",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "deposit_payment_cancelled_at",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "deposit_payment_failure_reason",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "remaining_payment_status",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "remaining_payment_order_code",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "remaining_payment_link_id",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "remaining_payment_checkout_url",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "remaining_payment_qr_code",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "remaining_payment_created_at",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "remaining_payment_paid_at",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "remaining_payment_cancelled_at",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "remaining_payment_failure_reason",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "refund_eligible_percent",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "refund_amount",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "refund_policy_note",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "refund_bank_bin",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "refund_account_number",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "refund_account_name",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "refund_reference_id",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "refund_payout_id",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "refund_status",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "refund_failure_reason",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "refund_requested_at",
                table: "custom_booking_quotes");

            migrationBuilder.DropColumn(
                name: "refund_processed_at",
                table: "custom_booking_quotes");
        }
    }
}
