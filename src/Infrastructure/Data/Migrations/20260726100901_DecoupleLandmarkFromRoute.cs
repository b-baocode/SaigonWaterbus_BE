using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DecoupleLandmarkFromRoute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_landmarks_routes_route_id",
                table: "landmarks");

            migrationBuilder.DropIndex(
                name: "IX_landmarks_route_id_display_order",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "route_id",
                table: "landmarks");

            migrationBuilder.CreateIndex(
                name: "IX_landmarks_is_active_display_order",
                table: "landmarks",
                columns: new[] { "is_active", "display_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_landmarks_is_active_display_order",
                table: "landmarks");

            migrationBuilder.AddColumn<Guid>(
                name: "route_id",
                table: "landmarks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_landmarks_route_id_display_order",
                table: "landmarks",
                columns: new[] { "route_id", "display_order" });

            migrationBuilder.AddForeignKey(
                name: "FK_landmarks_routes_route_id",
                table: "landmarks",
                column: "route_id",
                principalTable: "routes",
                principalColumn: "route_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
