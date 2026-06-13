using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeVesselServiceOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_vessels_waterbus_services_WaterbusServiceId",
                table: "vessels");

            migrationBuilder.AlterColumn<Guid>(
                name: "WaterbusServiceId",
                table: "vessels",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_vessels_waterbus_services_WaterbusServiceId",
                table: "vessels",
                column: "WaterbusServiceId",
                principalTable: "waterbus_services",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_vessels_waterbus_services_WaterbusServiceId",
                table: "vessels");

            migrationBuilder.AlterColumn<Guid>(
                name: "WaterbusServiceId",
                table: "vessels",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_vessels_waterbus_services_WaterbusServiceId",
                table: "vessels",
                column: "WaterbusServiceId",
                principalTable: "waterbus_services",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
