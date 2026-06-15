using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DecoupleVesselsFromServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_vessels_waterbus_services_WaterbusServiceId",
                table: "vessels");

            migrationBuilder.DropIndex(
                name: "IX_vessels_WaterbusServiceId",
                table: "vessels");

            migrationBuilder.DropColumn(
                name: "WaterbusServiceId",
                table: "vessels");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WaterbusServiceId",
                table: "vessels",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_vessels_WaterbusServiceId",
                table: "vessels",
                column: "WaterbusServiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_vessels_waterbus_services_WaterbusServiceId",
                table: "vessels",
                column: "WaterbusServiceId",
                principalTable: "waterbus_services",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
