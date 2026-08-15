using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripTimeCascadeTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cascade trip_stops theo delta của departure_time/arrival_time khi trip bị reschedule.
            // - departure_delta = NEW.departure_time - OLD.departure_time (nếu đổi)
            // - arrival_delta   = NEW.arrival_time   - OLD.arrival_time   (nếu đổi)
            // - planned_arrival_time, planned_departure_time, adjusted_arrival_time, adjusted_departure_time
            //   đều cộng cùng delta tương ứng.
            //
            // Bảo vệ:
            // - OLD.status = 'Arrived' / 'Cancelled' / 'Completed': không cascade (giữ dữ liệu lịch sử).
            // - cả 2 time cũ đều < now(): không cascade (admin sửa trip quá khứ, không được ghi đè).
            // Dùng OLD.status (không NEW.status) để tránh race với BEFORE trigger trg_sync_trip_status.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION trg_cascade_trip_stops_on_trip_update()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    dep_delta interval;
                    arr_delta interval;
                    now_utc timestamptz := now();
                BEGIN
                    IF OLD.status IN ('Arrived', 'Cancelled', 'Completed') THEN
                        RETURN NEW;
                    END IF;

                    IF OLD.departure_time < now_utc AND OLD.arrival_time < now_utc THEN
                        RETURN NEW;
                    END IF;

                    IF NEW.departure_time IS DISTINCT FROM OLD.departure_time THEN
                        dep_delta := NEW.departure_time - OLD.departure_time;
                        UPDATE trip_stops
                        SET planned_departure_time  = planned_departure_time + dep_delta,
                            adjusted_departure_time = COALESCE(adjusted_departure_time, planned_departure_time) + dep_delta,
                            updated_at              = now()
                        WHERE trip_id = NEW.trip_id;
                    END IF;

                    IF NEW.arrival_time IS DISTINCT FROM OLD.arrival_time THEN
                        arr_delta := NEW.arrival_time - OLD.arrival_time;
                        UPDATE trip_stops
                        SET planned_arrival_time  = planned_arrival_time + arr_delta,
                            adjusted_arrival_time = COALESCE(adjusted_arrival_time, planned_arrival_time) + arr_delta,
                            updated_at            = now()
                        WHERE trip_id = NEW.trip_id;
                    END IF;

                    RETURN NEW;
                END;
                $$;
            ");

            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_trips_cascade_stops ON trips;");

            migrationBuilder.Sql(@"
                CREATE TRIGGER trg_trips_cascade_stops
                AFTER UPDATE OF departure_time, arrival_time ON trips
                FOR EACH ROW
                EXECUTE FUNCTION trg_cascade_trip_stops_on_trip_update();
            ");

            // Cascade bookings theo departure_time mới của trip:
            // - departure_date = (NEW.departure_time ở UTC)::date
            // - start_time     = (NEW.departure_time ở UTC)::time
            // Áp dụng cho mọi booking của trip, kể cả Confirmed (policy: luôn cascade).
            // Không cascade khi booking Cancelled / Completed / Refunded.
            //
            // Bảo vệ giống stops.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION trg_cascade_trip_bookings_on_trip_update()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF OLD.status IN ('Arrived', 'Cancelled', 'Completed') THEN
                        RETURN NEW;
                    END IF;

                    IF OLD.departure_time < now() AND OLD.arrival_time < now() THEN
                        RETURN NEW;
                    END IF;

                    IF NEW.departure_time IS DISTINCT FROM OLD.departure_time THEN
                        UPDATE bookings
                        SET departure_date = (NEW.departure_time AT TIME ZONE 'UTC')::date,
                            start_time     = (NEW.departure_time AT TIME ZONE 'UTC')::time,
                            updated_at     = now()
                        WHERE trip_id = NEW.trip_id
                          AND status NOT IN ('Cancelled', 'Completed', 'Refunded');
                    END IF;

                    RETURN NEW;
                END;
                $$;
            ");

            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_trips_cascade_bookings ON trips;");

            migrationBuilder.Sql(@"
                CREATE TRIGGER trg_trips_cascade_bookings
                AFTER UPDATE OF departure_time, arrival_time ON trips
                FOR EACH ROW
                EXECUTE FUNCTION trg_cascade_trip_bookings_on_trip_update();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_trips_cascade_bookings ON trips;");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS trg_cascade_trip_bookings_on_trip_update();");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_trips_cascade_stops ON trips;");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS trg_cascade_trip_stops_on_trip_update();");
        }
    }
}