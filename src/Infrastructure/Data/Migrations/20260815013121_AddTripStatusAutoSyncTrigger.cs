using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripStatusAutoSyncTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Thêm cột audit last_status_changed_at để trigger đánh dấu thời điểm đổi status.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_status_changed_at",
                table: "trips",
                type: "timestamp with time zone",
                nullable: true);

            // 2) Auto-sync trips.status khi admin/staff chỉnh departure_time/arrival_time trực tiếp trong DB.
            //    Quy tắc:
            //      - status = 'Cancelled' (override thủ công): KHÔNG đụng.
            //      - Nếu arrival_time mới > now(): reset 'Arrived'/'Departed' về 'Scheduled' (reschedule về tương lai).
            //      - now() >= arrival_time mới    -> 'Arrived'  (Completed)
            //      - now() >= departure_time mới  -> 'Departed' (InProgress)
            //      - ngược lại                    -> 'Scheduled'
            //    Trigger BEFORE UPDATE để NEW.* đã có giá trị mới sau khi EF / raw SQL gán.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION trg_sync_trip_status()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    now_utc timestamptz := now() AT TIME ZONE 'UTC';
                    new_status text;
                    status_changed boolean := false;
                    computed_status text;
                BEGIN
                    -- Chỉ auto-sync khi departure/arrival thực sự thay đổi
                    IF NEW.departure_time IS NOT DISTINCT FROM OLD.departure_time
                       AND NEW.arrival_time   IS NOT DISTINCT FROM OLD.arrival_time THEN
                        RETURN NEW;
                    END IF;

                    -- Không đụng vào override thủ công của admin/staff
                    IF NEW.status = 'Cancelled' THEN
                        RETURN NEW;
                    END IF;

                    -- Tính status mục tiêu theo thời điểm hiện tại
                    IF NEW.arrival_time IS NOT NULL AND now_utc >= NEW.arrival_time THEN
                        new_status := 'Arrived';
                    ELSIF NEW.departure_time IS NOT NULL AND now_utc >= NEW.departure_time THEN
                        new_status := 'Departed';
                    ELSE
                        new_status := 'Scheduled';
                    END IF;

                    -- Cho phép reset 'Arrived'/'Departed' về 'Scheduled' khi reschedule cả 2 time sang tương lai
                    IF NEW.arrival_time IS NOT NULL
                       AND NEW.departure_time IS NOT NULL
                       AND NEW.arrival_time > now_utc
                       AND NEW.departure_time > now_utc THEN
                        computed_status := 'Scheduled';
                    ELSE
                        computed_status := new_status;
                    END IF;

                    -- Áp dụng nếu khác giá trị hiện tại
                    IF NEW.status IS DISTINCT FROM computed_status THEN
                        NEW.status := computed_status;
                        status_changed := true;
                    END IF;

                    -- Đánh dấu thời điểm đổi status để phục vụ audit
                    IF status_changed THEN
                        NEW.last_status_changed_at := now_utc;
                    ELSE
                        NEW.last_status_changed_at := OLD.last_status_changed_at;
                    END IF;

                    RETURN NEW;
                END;
                $$;
            ");

            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_trips_status_autosync ON trips;");

            migrationBuilder.Sql(@"
                CREATE TRIGGER trg_trips_status_autosync
                BEFORE UPDATE OF departure_time, arrival_time ON trips
                FOR EACH ROW
                EXECUTE FUNCTION trg_sync_trip_status();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_trips_status_autosync ON trips;");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS trg_sync_trip_status();");
            migrationBuilder.DropColumn(
                name: "last_status_changed_at",
                table: "trips");
        }
    }
}