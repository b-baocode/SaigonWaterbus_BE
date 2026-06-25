using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeatTypeCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "seat_type_code",
                table: "vessel_seats",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "STANDARD");

            migrationBuilder.Sql(
                """
                UPDATE vessel_seats SET seat_type_code = CASE UPPER(seat_type)
                    WHEN 'STANDARD' THEN 'STANDARD'
                    WHEN 'CABIN' THEN 'CABIN'
                    WHEN 'RIVER' THEN 'RIVER'
                    WHEN 'SKY' THEN 'SKY'
                    WHEN 'VIP' THEN 'CABIN'
                    ELSE 'STANDARD'
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "seat_type_code",
                table: "vessel_seats");
        }
    }
}
