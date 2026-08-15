using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripPastTimeGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ghi đè function trg_sync_trip_status để thêm guard chặn UPDATE
            // departure_time/arrival_time về quá khứ.
            //   - Mặc định: từ chối với RAISE EXCEPTION nếu NEW.departure_time < now().
            //   - Bypass cho data migration: SET LOCAL app.allow_trip_past_time = 'on'.
            //
            // Lưu ý: DROP trigger cũ trg_trips_status_autosync (nếu còn) vì nó duplicate
            // chức năng với trigger mới trg_sync_trip_status_trigger (đã cài ở migration trước).
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION trg_sync_trip_status()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    now_utc timestamptz := now() AT TIME ZONE 'UTC';
                    new_status text;
                    status_changed boolean := false;
                    computed_status text;
                    allow_past boolean;
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

                    -- Bypass: SET LOCAL app.allow_trip_past_time = 'on' (data migration / backfill).
                    allow_past := current_setting('app.allow_trip_past_time', true) = 'on';

                    IF NOT allow_past THEN
                        IF NEW.departure_time IS NOT NULL AND NEW.departure_time < now_utc THEN
                            RAISE EXCEPTION 'departure_time (%) không được nằm trong quá khứ (now=%). '
                                'Để backdate data, set: SET LOCAL app.allow_trip_past_time = ''on'';',
                                NEW.departure_time, now_utc
                                USING ERRCODE = 'check_violation';
                        END IF;
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
                $function$;
            ");

            // Đảm bảo đúng 1 trigger BEFORE UPDATE chạy function này (DROP trùng nếu có).
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_trips_status_autosync ON trips;");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_sync_trip_status_trigger ON trips;");
            migrationBuilder.Sql(@"
                CREATE TRIGGER trg_sync_trip_status_trigger
                BEFORE UPDATE OF departure_time, arrival_time ON trips
                FOR EACH ROW
                EXECUTE FUNCTION trg_sync_trip_status();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback về function không có past_block (giữ nguyên trigger).
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION trg_sync_trip_status()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    now_utc timestamptz := now() AT TIME ZONE 'UTC';
                    new_status text;
                    status_changed boolean := false;
                    computed_status text;
                BEGIN
                    IF NEW.departure_time IS NOT DISTINCT FROM OLD.departure_time
                       AND NEW.arrival_time   IS NOT DISTINCT FROM OLD.arrival_time THEN
                        RETURN NEW;
                    END IF;

                    IF NEW.status = 'Cancelled' THEN
                        RETURN NEW;
                    END IF;

                    IF NEW.arrival_time IS NOT NULL AND now_utc >= NEW.arrival_time THEN
                        new_status := 'Arrived';
                    ELSIF NEW.departure_time IS NOT NULL AND now_utc >= NEW.departure_time THEN
                        new_status := 'Departed';
                    ELSE
                        new_status := 'Scheduled';
                    END IF;

                    IF NEW.arrival_time IS NOT NULL
                       AND NEW.departure_time IS NOT NULL
                       AND NEW.arrival_time > now_utc
                       AND NEW.departure_time > now_utc THEN
                        computed_status := 'Scheduled';
                    ELSE
                        computed_status := new_status;
                    END IF;

                    IF NEW.status IS DISTINCT FROM computed_status THEN
                        NEW.status := computed_status;
                        status_changed := true;
                    END IF;

                    IF status_changed THEN
                        NEW.last_status_changed_at := now_utc;
                    ELSE
                        NEW.last_status_changed_at := OLD.last_status_changed_at;
                    END IF;

                    RETURN NEW;
                END;
                $function$;
            ");
        }
    }
}