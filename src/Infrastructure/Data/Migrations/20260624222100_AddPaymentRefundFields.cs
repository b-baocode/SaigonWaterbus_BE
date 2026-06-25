using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRefundFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "refund_amount",
                table: "payments",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "refund_failure_reason",
                table: "payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_payout_id",
                table: "payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_reference_id",
                table: "payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_status",
                table: "payments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refunded_at",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "refund_amount",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refund_failure_reason",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refund_payout_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refund_reference_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refund_status",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refunded_at",
                table: "payments");
        }
    }
}
