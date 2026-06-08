using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoreStatusesAsText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE vessels
                ALTER COLUMN "Status" TYPE character varying(32)
                USING CASE "Status"
                    WHEN 1 THEN 'Active'
                    WHEN 2 THEN 'Maintenance'
                    WHEN 3 THEN 'Inactive'
                    WHEN 4 THEN 'Retired'
                    ELSE 'Active'
                END;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE users
                ALTER COLUMN "Status" TYPE character varying(32)
                USING CASE "Status"
                    WHEN 0 THEN 'PendingVerification'
                    WHEN 1 THEN 'Active'
                    WHEN 2 THEN 'Suspended'
                    ELSE 'PendingVerification'
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE vessels
                ALTER COLUMN "Status" TYPE integer
                USING CASE "Status"
                    WHEN 'Active' THEN 1
                    WHEN 'Maintenance' THEN 2
                    WHEN 'Inactive' THEN 3
                    WHEN 'Retired' THEN 4
                    ELSE 1
                END;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE users
                ALTER COLUMN "Status" TYPE integer
                USING CASE "Status"
                    WHEN 'PendingVerification' THEN 0
                    WHEN 'Active' THEN 1
                    WHEN 'Suspended' THEN 2
                    ELSE 0
                END;
                """);
        }
    }
}
