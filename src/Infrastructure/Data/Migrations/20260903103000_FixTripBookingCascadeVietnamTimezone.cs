using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations;

/// <summary>
/// Booking departure_date/start_time are stored as Vietnam-local values. The trip timestamp is
/// timestamptz, so the cascade trigger must convert it to Asia/Ho_Chi_Minh instead of UTC.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260903103000_FixTripBookingCascadeVietnamTimezone")]
public sealed class FixTripBookingCascadeVietnamTimezone : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(CreateFunctionSql("Asia/Ho_Chi_Minh"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(CreateFunctionSql("UTC"));
    }

    private static string CreateFunctionSql(string timeZone) => $$"""
        CREATE OR REPLACE FUNCTION trg_cascade_trip_bookings_on_trip_update()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $function$
        BEGIN
            IF OLD.status IN ('Arrived', 'Cancelled', 'Completed') THEN
                RETURN NEW;
            END IF;

            IF OLD.departure_time < now() AND OLD.arrival_time < now() THEN
                RETURN NEW;
            END IF;

            IF NEW.departure_time IS DISTINCT FROM OLD.departure_time THEN
                UPDATE bookings
                SET departure_date = (NEW.departure_time AT TIME ZONE '{{timeZone}}')::date,
                    start_time     = (NEW.departure_time AT TIME ZONE '{{timeZone}}')::time,
                    updated_at     = now()
                WHERE trip_id = NEW.trip_id
                  AND status NOT IN ('Cancelled', 'Completed', 'Refunded');
            END IF;

            RETURN NEW;
        END;
        $function$;
        """;
}
