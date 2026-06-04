using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSpatialSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.AddColumn<Point>(
                name: "location",
                table: "stations",
                type: "geography(Point,4326)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "osm_id",
                table: "stations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "osm_id",
                table: "routes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<LineString>(
                name: "route_geometry",
                table: "routes",
                type: "geography(LineString,4326)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_stations_location",
                table: "stations",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_stations_osm_id",
                table: "stations",
                column: "osm_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stations_location",
                table: "stations");

            migrationBuilder.DropIndex(
                name: "ix_stations_osm_id",
                table: "stations");

            migrationBuilder.DropColumn(
                name: "location",
                table: "stations");

            migrationBuilder.DropColumn(
                name: "osm_id",
                table: "stations");

            migrationBuilder.DropColumn(
                name: "osm_id",
                table: "routes");

            migrationBuilder.DropColumn(
                name: "route_geometry",
                table: "routes");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");
        }
    }
}
