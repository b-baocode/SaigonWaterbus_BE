using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionRuleEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Thêm các cột mới trước (để còn backfill dữ liệu từ cột cũ).
            migrationBuilder.AddColumn<decimal>(
                name: "budget_cap",
                table: "promotions",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                table: "promotions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "promotions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "first_booking_only",
                table: "promotions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "image_url",
                table: "promotions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "max_discount_amount",
                table: "promotions",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_uses_per_account",
                table: "promotions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scope",
                table: "promotions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "promotions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "visibility",
                table: "promotions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Public");

            // 2) Backfill dữ liệu cũ sang mô hình mới TRƯỚC khi drop cột.
            //    OncePerAccount -> max_uses_per_account = 1; MultiplePerAccount -> NULL (không giới hạn).
            migrationBuilder.Sql(
                "UPDATE promotions SET max_uses_per_account = 1 WHERE account_usage_policy = 'OncePerAccount';");

            //    status cũ chỉ có 'Active' | 'Inactive'. 'Active' khớp enum; 'Inactive' -> 'Paused'
            //    (giữ khả năng bật lại, không map thẳng sang Archived vì không rõ tắt tạm hay tắt hẳn).
            migrationBuilder.Sql(
                "UPDATE promotions SET status = 'Paused' WHERE status = 'Inactive';");

            // 3) Thu nhỏ cột status và drop các cột không còn dùng.
            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "promotions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Draft",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.DropColumn(
                name: "account_usage_policy",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "usage_count",
                table: "promotions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "budget_cap",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "description",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "first_booking_only",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "image_url",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "max_discount_amount",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "max_uses_per_account",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "scope",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "visibility",
                table: "promotions");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "promotions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Draft");

            migrationBuilder.AddColumn<string>(
                name: "account_usage_policy",
                table: "promotions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "MultiplePerAccount");

            migrationBuilder.AddColumn<int>(
                name: "usage_count",
                table: "promotions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
