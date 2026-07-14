using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(global::SaigonWaterbus.Infrastructure.Data.ApplicationDbContext))]
    [Migration("20260714090000_RemoveTripFromStaffWorkAssignments")]
    public partial class RemoveTripFromStaffWorkAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE staff_work_assignments
                    DROP CONSTRAINT IF EXISTS "FK_staff_work_assignments_trips_trip_id";

                DROP INDEX IF EXISTS "IX_staff_work_assignments_assignment_type_trip_id_status";
                DROP INDEX IF EXISTS "IX_staff_work_assignments_trip_id";

                ALTER TABLE staff_work_assignments
                    DROP COLUMN IF EXISTS trip_id;

                UPDATE staff_work_assignments
                SET status = 'Scheduled'
                WHERE status IN ('Active', 'Completed');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE staff_work_assignments
                    ADD COLUMN IF NOT EXISTS trip_id uuid;

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conname = 'FK_staff_work_assignments_trips_trip_id'
                    ) THEN
                        ALTER TABLE staff_work_assignments
                            ADD CONSTRAINT "FK_staff_work_assignments_trips_trip_id"
                            FOREIGN KEY (trip_id) REFERENCES trips (trip_id) ON DELETE SET NULL;
                    END IF;
                END $$;

                CREATE INDEX IF NOT EXISTS "IX_staff_work_assignments_trip_id"
                    ON staff_work_assignments (trip_id);

                CREATE INDEX IF NOT EXISTS "IX_staff_work_assignments_assignment_type_trip_id_status"
                    ON staff_work_assignments (assignment_type, trip_id, status);
                """);
        }
    }
}
