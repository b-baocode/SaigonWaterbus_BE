using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChangeRouteStopStandardTravelMinToDecimal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "standard_travel_min",
                table: "route_stops",
                type: "numeric(8,2)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "standard_travel_min",
                table: "route_stops",
                type: "integer",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldNullable: true);
        }
    }
}
