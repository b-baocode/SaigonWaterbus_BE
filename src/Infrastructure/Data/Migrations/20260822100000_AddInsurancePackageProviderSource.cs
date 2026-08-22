using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInsurancePackageProviderSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider_source",
                table: "insurance_packages",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "ThirdParty");

            // Backfill an toàn:
            //  - Gói đã đánh dấu IsWaterbusDefault = true  -> Waterbus (hệ thống tự gắn)
            //  - Còn lại                                  -> ThirdParty (mặc định cột)
            migrationBuilder.Sql(
                "UPDATE insurance_packages SET provider_source = 'Waterbus' WHERE \"IsWaterbusDefault\" = true;");

            migrationBuilder.CreateIndex(
                name: "IX_insurance_packages_BookingType_ProviderSource_IsActive",
                table: "insurance_packages",
                columns: new[] { "booking_type", "provider_source", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_insurance_packages_BookingType_ProviderSource_IsActive",
                table: "insurance_packages");

            migrationBuilder.DropColumn(
                name: "provider_source",
                table: "insurance_packages");
        }
    }
}