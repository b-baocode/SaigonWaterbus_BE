using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStationImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "station_images",
                columns: table => new
                {
                    station_image_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    public_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_station_images", x => x.station_image_id);
                    table.ForeignKey(
                        name: "FK_station_images_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO station_images (station_image_id, station_id, url, public_id, display_order, is_primary, created_at, updated_at)
                SELECT gen_random_uuid(), s.station_id, s.image_url, s.image_public_id, 1, TRUE, now(), now()
                FROM stations s
                WHERE s.image_url IS NOT NULL
                  AND length(trim(s.image_url)) > 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_station_images_station_id_display_order",
                table: "station_images",
                columns: new[] { "station_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_station_images_station_id_is_primary",
                table: "station_images",
                columns: new[] { "station_id", "is_primary" },
                unique: true,
                filter: "is_primary = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "station_images");
        }
    }
}
