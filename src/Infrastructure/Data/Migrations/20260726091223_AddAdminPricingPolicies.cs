using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminPricingPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "charter_boat_rental_price_policies",
                columns: table => new
                {
                    charter_boat_rental_price_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number_of_decks = table.Column<int>(type: "integer", nullable: false),
                    rental_unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charter_boat_rental_price_policies", x => x.charter_boat_rental_price_policy_id);
                });

            migrationBuilder.CreateTable(
                name: "ticket_fare_rules",
                columns: table => new
                {
                    ticket_fare_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_type_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    route_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    price_modifier = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_fare_rules", x => x.ticket_fare_rule_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_charter_boat_rental_price_policies_number_of_decks_rental_u~",
                table: "charter_boat_rental_price_policies",
                columns: new[] { "number_of_decks", "rental_unit" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ticket_fare_rules_ticket_type_code_route_type",
                table: "ticket_fare_rules",
                columns: new[] { "ticket_type_code", "route_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "charter_boat_rental_price_policies");

            migrationBuilder.DropTable(
                name: "ticket_fare_rules");
        }
    }
}
