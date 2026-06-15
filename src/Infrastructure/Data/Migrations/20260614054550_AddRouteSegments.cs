using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteSegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "route_segments",
                columns: table => new
                {
                    route_segment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    segment_order = table.Column<int>(type: "integer", nullable: false),
                    distance_km = table.Column<decimal>(type: "numeric(8,2)", nullable: false),
                    estimated_travel_minutes = table.Column<int>(type: "integer", nullable: false),
                    geometry = table.Column<LineString>(type: "geography(LineString,4326)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_route_segments", x => x.route_segment_id);
                    table.ForeignKey(
                        name: "FK_route_segments_routes_route_id",
                        column: x => x.route_id,
                        principalTable: "routes",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_route_segments_stations_from_station_id",
                        column: x => x.from_station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_route_segments_stations_to_station_id",
                        column: x => x.to_station_id,
                        principalTable: "stations",
                        principalColumn: "station_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_route_segments_from_station_id",
                table: "route_segments",
                column: "from_station_id");

            migrationBuilder.CreateIndex(
                name: "ix_route_segments_geometry",
                table: "route_segments",
                column: "geometry")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_route_segments_route_id_from_station_id_to_station_id",
                table: "route_segments",
                columns: new[] { "route_id", "from_station_id", "to_station_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_route_segments_route_id_segment_order",
                table: "route_segments",
                columns: new[] { "route_id", "segment_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_route_segments_to_station_id",
                table: "route_segments",
                column: "to_station_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "route_segments");
        }
    }
}
