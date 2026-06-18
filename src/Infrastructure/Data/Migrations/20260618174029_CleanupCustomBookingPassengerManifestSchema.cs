using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CleanupCustomBookingPassengerManifestSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE custom_booking_passengers DROP COLUMN IF EXISTS emergency_contact_name;
                ALTER TABLE custom_booking_passengers DROP COLUMN IF EXISTS emergency_contact_phone;
                ALTER TABLE custom_booking_passengers DROP COLUMN IF EXISTS guardian_name;
                ALTER TABLE custom_booking_passengers DROP COLUMN IF EXISTS guardian_phone;
                ALTER TABLE custom_booking_passengers DROP COLUMN IF EXISTS health_note;
                ALTER TABLE custom_booking_requests ALTER COLUMN passenger_manifest_status DROP DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE custom_booking_requests ALTER COLUMN passenger_manifest_status SET DEFAULT 'NotStarted';
                ALTER TABLE custom_booking_passengers ADD COLUMN IF NOT EXISTS emergency_contact_name character varying(150);
                ALTER TABLE custom_booking_passengers ADD COLUMN IF NOT EXISTS emergency_contact_phone character varying(20);
                ALTER TABLE custom_booking_passengers ADD COLUMN IF NOT EXISTS guardian_name character varying(150);
                ALTER TABLE custom_booking_passengers ADD COLUMN IF NOT EXISTS guardian_phone character varying(20);
                ALTER TABLE custom_booking_passengers ADD COLUMN IF NOT EXISTS health_note character varying(500);
                """);
        }
    }
}
