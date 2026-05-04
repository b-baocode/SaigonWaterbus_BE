using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SaigonWaterbus.Infrastructure.Data;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260504103000_NormalizePhoneNumbersToE164")]
    public partial class NormalizePhoneNumbersToE164 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE users
                SET "NormalizedPhoneNumber" = '+84' || substring("NormalizedPhoneNumber" from 2)
                WHERE "NormalizedPhoneNumber" ~ '^0[0-9]{9}$';
                """);

            migrationBuilder.Sql(
                """
                UPDATE users
                SET "PhoneNumber" = '+84' || substring("PhoneNumber" from 2)
                WHERE "PhoneNumber" ~ '^0[0-9]{9}$';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE users
                SET "NormalizedPhoneNumber" = '0' || substring("NormalizedPhoneNumber" from 4)
                WHERE "NormalizedPhoneNumber" ~ '^\+84[0-9]{9}$';
                """);

            migrationBuilder.Sql(
                """
                UPDATE users
                SET "PhoneNumber" = '0' || substring("PhoneNumber" from 4)
                WHERE "PhoneNumber" ~ '^\+84[0-9]{9}$';
                """);
        }
    }
}
