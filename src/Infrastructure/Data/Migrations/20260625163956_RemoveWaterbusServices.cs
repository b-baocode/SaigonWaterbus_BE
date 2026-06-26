using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveWaterbusServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS services;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    service_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    service_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_services", x => x.service_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_services_display_order",
                table: "services",
                column: "display_order");

            migrationBuilder.CreateIndex(
                name: "IX_services_service_code",
                table: "services",
                column: "service_code",
                unique: true);
        }
    }
}
