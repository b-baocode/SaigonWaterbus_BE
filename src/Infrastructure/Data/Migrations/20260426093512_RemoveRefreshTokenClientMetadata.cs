using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRefreshTokenClientMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceName",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "refresh_tokens");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceName",
                table: "refresh_tokens",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "refresh_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "refresh_tokens",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
