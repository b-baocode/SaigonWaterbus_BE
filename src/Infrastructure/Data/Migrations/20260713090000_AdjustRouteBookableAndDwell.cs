using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AdjustRouteBookableAndDwell : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "is_bookable",
            table: "routes",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<string>(
            name: "route_type",
            table: "routes",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "Regular");

        migrationBuilder.DropColumn(
            name: "standard_dwell_min",
            table: "route_stops");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "standard_dwell_min",
            table: "route_stops",
            type: "integer",
            nullable: true);

        migrationBuilder.DropColumn(
            name: "is_bookable",
            table: "routes");

        migrationBuilder.DropColumn(
            name: "route_type",
            table: "routes");
    }
}
