using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeatTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "seat_type",
                table: "seats");

            migrationBuilder.AddColumn<Guid>(
                name: "seat_type_id",
                table: "seats",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "seat_types",
                columns: table => new
                {
                    seat_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    base_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seat_types", x => x.seat_type_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_seats_seat_type_id",
                table: "seats",
                column: "seat_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_seat_types_code",
                table: "seat_types",
                column: "code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_seats_seat_types_seat_type_id",
                table: "seats",
                column: "seat_type_id",
                principalTable: "seat_types",
                principalColumn: "seat_type_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_seats_seat_types_seat_type_id",
                table: "seats");

            migrationBuilder.DropTable(
                name: "seat_types");

            migrationBuilder.DropIndex(
                name: "IX_seats_seat_type_id",
                table: "seats");

            migrationBuilder.DropColumn(
                name: "seat_type_id",
                table: "seats");

            migrationBuilder.AddColumn<string>(
                name: "seat_type",
                table: "seats",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");
        }
    }
}
