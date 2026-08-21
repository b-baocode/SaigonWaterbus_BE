using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCharterBookingAutoDeleteTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Tạo bảng log để track các booking bị xóa tự động
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS charter_booking_deleted_logs (
                    id bigserial PRIMARY KEY,
                    booking_id uuid NOT NULL,
                    booking_code varchar(50),
                    departure_date date,
                    start_time time without time zone,
                    deleted_at timestamptz DEFAULT now(),
                    reason varchar(200) DEFAULT 'Auto-delete after 12h overdue'
                );
            ");

            // 2. Tạo function cho trigger - tự động xóa charter booking quá 12h khi bị đánh dấu Overdue (status = 7)
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION trg_auto_delete_overdue_charter_booking()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    overdue_threshold timestamptz;
                BEGIN
                    -- Chỉ chạy khi booking được đánh dấu Overdue (status = 7)
                    -- và trước đó không phải Overdue
                    IF NEW.status = 7 AND OLD.status != 7 AND NEW.booking_type = 'CharterBooking' THEN
                        -- Tính thời điểm xóa: departure_time (VN) + 30 phút (overdue) + 12 tiếng
                        IF NEW.departure_date IS NOT NULL AND NEW.start_time IS NOT NULL THEN
                            overdue_threshold := (
                                NEW.departure_date::timestamptz
                                + NEW.start_time::interval
                                + interval '7 hours'  -- VN timezone +07:00
                                + interval '30 minutes'  -- overdue grace period
                                + interval '12 hours'  -- auto-delete sau 12h
                            );

                            -- Log trước khi xóa
                            INSERT INTO charter_booking_deleted_logs (booking_id, booking_code, departure_date, start_time, reason)
                            VALUES (NEW.id, NEW.booking_code, NEW.departure_date, NEW.start_time, 
                                    'Auto-delete after 12h overdue (threshold: ' || overdue_threshold || ')');

                            -- Xóa booking khi đã quá ngưỡng
                            IF now() > overdue_threshold THEN
                                DELETE FROM bookings WHERE id = NEW.id;
                            END IF;
                        END IF;
                    END IF;

                    RETURN NEW;
                END;
                $$;
            ");

            // 3. Tạo trigger trên bảng bookings
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_charter_bookings_auto_delete ON bookings;
                
                CREATE TRIGGER trg_charter_bookings_auto_delete
                AFTER UPDATE OF status ON bookings
                FOR EACH ROW
                EXECUTE FUNCTION trg_auto_delete_overdue_charter_booking();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_charter_bookings_auto_delete ON bookings;");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS trg_auto_delete_overdue_charter_booking();");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS charter_booking_deleted_logs;");
        }
    }
}
