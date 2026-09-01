using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations;

/// <summary>
/// Booking, ticket và payment là dữ liệu tài chính/audit nên không được hard-delete.
/// Việc dọn dữ liệu vận hành charter được thực hiện bởi CharterTripExpirationHostedService.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260901123000_DisableLegacyCharterBookingAutoDeleteTrigger")]
public sealed class DisableLegacyCharterBookingAutoDeleteTrigger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_charter_bookings_auto_delete ON bookings;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS trg_auto_delete_overdue_charter_booking();");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Không khôi phục trigger cũ vì nó xóa cascade ticket/payment và làm mất dữ liệu doanh thu.
    }
}
