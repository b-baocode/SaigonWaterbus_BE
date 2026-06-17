using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveServiceSeatTypePrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_seat_type_prices");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "service_seat_type_prices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeatTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    WaterbusServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    PriceModifier = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 1m)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_seat_type_prices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_service_seat_type_prices_seat_types_SeatTypeId",
                        column: x => x.SeatTypeId,
                        principalTable: "seat_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_seat_type_prices_waterbus_services_WaterbusServiceId",
                        column: x => x.WaterbusServiceId,
                        principalTable: "waterbus_services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_service_seat_type_prices_SeatTypeId",
                table: "service_seat_type_prices",
                column: "SeatTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_service_seat_type_prices_WaterbusServiceId_SeatTypeId",
                table: "service_seat_type_prices",
                columns: new[] { "WaterbusServiceId", "SeatTypeId" },
                unique: true);
        }
    }
}
