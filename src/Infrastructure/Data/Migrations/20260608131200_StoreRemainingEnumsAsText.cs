using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoreRemainingEnumsAsText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "vessel_facilities"
                ALTER COLUMN "Type" TYPE character varying(32)
                USING CASE "Type"
                    WHEN 1 THEN 'Toilet'
                    ELSE "Type"::text
                END;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "users"
                ALTER COLUMN "AvatarSource" TYPE character varying(32)
                USING CASE "AvatarSource"
                    WHEN 0 THEN 'None'
                    WHEN 1 THEN 'Google'
                    WHEN 2 THEN 'Upload'
                    ELSE "AvatarSource"::text
                END;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "otp_challenges"
                ALTER COLUMN "Purpose" TYPE character varying(32)
                USING CASE "Purpose"
                    WHEN 1 THEN 'Register'
                    WHEN 2 THEN 'ForgotPassword'
                    WHEN 3 THEN 'EmailChange'
                    WHEN 4 THEN 'PhoneChange'
                    ELSE "Purpose"::text
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "vessel_facilities"
                ALTER COLUMN "Type" TYPE integer
                USING CASE "Type"
                    WHEN 'Toilet' THEN 1
                    ELSE "Type"::integer
                END;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "users"
                ALTER COLUMN "AvatarSource" TYPE integer
                USING CASE "AvatarSource"
                    WHEN 'None' THEN 0
                    WHEN 'Google' THEN 1
                    WHEN 'Upload' THEN 2
                    ELSE "AvatarSource"::integer
                END;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "otp_challenges"
                ALTER COLUMN "Purpose" TYPE integer
                USING CASE "Purpose"
                    WHEN 'Register' THEN 1
                    WHEN 'ForgotPassword' THEN 2
                    WHEN 'EmailChange' THEN 3
                    WHEN 'PhoneChange' THEN 4
                    ELSE "Purpose"::integer
                END;
                """);
        }
    }
}
