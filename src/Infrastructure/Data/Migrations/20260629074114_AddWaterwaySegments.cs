using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWaterwaySegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "waterway_segments",
                columns: table => new
                {
                    waterway_segment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    osm_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    waterway_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    waterway_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    segment_order = table.Column<int>(type: "integer", nullable: false),
                    length_km = table.Column<decimal>(type: "numeric(8,2)", nullable: false),
                    geometry = table.Column<LineString>(type: "geography(LineString,4326)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waterway_segments", x => x.waterway_segment_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_waterway_segments_geometry",
                table: "waterway_segments",
                column: "geometry")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_waterway_segments_name",
                table: "waterway_segments",
                column: "waterway_name");

            migrationBuilder.CreateIndex(
                name: "ix_waterway_segments_osm_id",
                table: "waterway_segments",
                column: "osm_id");

            migrationBuilder.CreateIndex(
                name: "IX_waterway_segments_osm_id_segment_order",
                table: "waterway_segments",
                columns: new[] { "osm_id", "segment_order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "waterway_segments");
        }
    }
}
