using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVessels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vessels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WaterbusServiceId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RegistrationNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SeatCount = table.Column<int>(type: "integer", nullable: false),
                    NumberOfDecks = table.Column<int>(type: "integer", nullable: false),
                    MaxSpeedKmh = table.Column<int>(type: "integer", nullable: true),
                    YearBuilt = table.Column<int>(type: "integer", nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ImagePublicId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vessels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vessels_waterbus_services_WaterbusServiceId",
                        column: x => x.WaterbusServiceId,
                        principalTable: "waterbus_services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_vessels_Code",
                table: "vessels",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vessels_RegistrationNumber",
                table: "vessels",
                column: "RegistrationNumber",
                unique: true,
                filter: "\"RegistrationNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_vessels_Status",
                table: "vessels",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_vessels_WaterbusServiceId",
                table: "vessels",
                column: "WaterbusServiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vessels");
        }
    }
}
