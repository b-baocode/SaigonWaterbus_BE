using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChangeRouteEstimatedDurationMinToDecimal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "estimated_duration_min",
                table: "routes",
                type: "numeric(8,2)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.Sql(
                """
                UPDATE routes AS r
                SET estimated_duration_min = totals.stop_sum
                FROM (
                    SELECT route_id,
                           ROUND(SUM(COALESCE(standard_travel_min, 0))::numeric, 2) AS stop_sum
                    FROM route_stops
                    GROUP BY route_id
                ) AS totals
                WHERE r.route_id = totals.route_id
                  AND totals.stop_sum > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "estimated_duration_min",
                table: "routes",
                type: "integer",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldNullable: true);
        }
    }
}
