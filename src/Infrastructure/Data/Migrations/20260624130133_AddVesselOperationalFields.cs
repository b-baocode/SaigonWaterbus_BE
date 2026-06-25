using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVesselOperationalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "image_public_id",
                table: "vessels",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_speed_kmh",
                table: "vessels",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "seats_configured",
                table: "vessels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "year_built",
                table: "vessels",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image_public_id",
                table: "vessels");

            migrationBuilder.DropColumn(
                name: "max_speed_kmh",
                table: "vessels");

            migrationBuilder.DropColumn(
                name: "seats_configured",
                table: "vessels");

            migrationBuilder.DropColumn(
                name: "year_built",
                table: "vessels");
        }
    }
}
