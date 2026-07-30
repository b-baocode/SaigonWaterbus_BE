using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using SaigonWaterbus.Application.Reports;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Reports : IEndpointGroup
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static string RoutePrefix => "/api/reports";

    public static string OpenApiTag => "Reports";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetRevenue, "revenue")
            .RequireAuthorization()
            .WithSummary("Bao cao doanh thu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager",
                null,
                "Tính theo Payments đã Paid trong khoảng fromDate/toDate.",
                "fromDate/toDate dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy; toDate là ngày cuối được tính.",
                "Nếu bỏ ngày thì mặc định lấy từ đầu tháng hiện tại đến hôm nay theo giờ Việt Nam.",
                "serviceType optional: Waterbus | Sightseeing | Charter.",
                "paymentMethod optional: Cash | BankTransfer | PayOs | Free.",
                "Doanh thu ròng = tổng payment đã thu - refundAmount."));

        group.MapGet(GetBookings, "bookings")
            .RequireAuthorization()
            .WithSummary("Tong hop / quan ly booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Staff chỉ thấy booking bán tại quầy của chính mình; Admin/Manager thấy toàn bộ.",
                "keyword tìm theo bookingCode/contactName/contactPhone/contactEmail.",
                "bookingStatus optional: PendingPayment | Confirmed | Cancelled | Expired | Refunded | Completed | ...",
                "paymentStatus optional: Unpaid | DepositPaid | Paid | Refunded | PartiallyRefunded.",
                "serviceType optional: Waterbus | Sightseeing | Charter.",
                "paymentMethod optional: Cash | BankTransfer | PayOs | Free.",
                "createdFrom/createdTo và departureFrom/departureTo dạng ngày; *To là ngày cuối được tính.",
                "Response có summary và items để FE làm bảng tổng hợp/chọn booking."));

        group.MapGet(GetBookingSelectOptions, "bookings/select")
            .RequireAuthorization()
            .WithSummary("Danh sach booking cho select/dropdown")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Dùng cho FE autocomplete/select booking.",
                "Staff chỉ thấy booking bán tại quầy của chính mình.",
                "Query: keyword, bookingStatus, paymentStatus, serviceType, limit."));
    }

    private static async Task<IResult> GetRevenue(
        ISender sender,
        [FromQuery] string? fromDate,
        [FromQuery] string? toDate,
        [FromQuery] string? serviceType,
        [FromQuery] string? paymentMethod,
        [FromQuery] Guid? soldByStaffId,
        CancellationToken cancellationToken)
    {
        if (!TryParseOptionalDateOnly(fromDate, out var from))
        {
            return Results.BadRequest(new { message = "fromDate phải là ngày, định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        if (!TryParseOptionalDateOnly(toDate, out var to))
        {
            return Results.BadRequest(new { message = "toDate phải là ngày, định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(VietnamOffset).DateTime);
        var startDate = from ?? new DateOnly(today.Year, today.Month, 1);
        var endDate = to ?? today;
        if (endDate < startDate)
        {
            return Results.BadRequest(new { message = "toDate phải lớn hơn hoặc bằng fromDate." });
        }

        return Results.Ok(await sender.Send(
            new GetRevenueReportQuery(
                ToVietnamStartOfDay(startDate),
                ToVietnamStartOfDay(endDate.AddDays(1)),
                serviceType,
                paymentMethod,
                soldByStaffId),
            cancellationToken));
    }

    private static async Task<IResult> GetBookings(
        ISender sender,
        [FromQuery] string? keyword,
        [FromQuery] string? bookingStatus,
        [FromQuery] string? paymentStatus,
        [FromQuery] string? serviceType,
        [FromQuery] string? paymentMethod,
        [FromQuery] Guid? soldByStaffId,
        [FromQuery] string? createdFrom,
        [FromQuery] string? createdTo,
        [FromQuery] string? departureFrom,
        [FromQuery] string? departureTo,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        if (!TryParseOptionalDateOnly(createdFrom, out var createdFromDate))
        {
            return Results.BadRequest(new { message = "createdFrom phải là ngày, định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        if (!TryParseOptionalDateOnly(createdTo, out var createdToDate))
        {
            return Results.BadRequest(new { message = "createdTo phải là ngày, định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        if (!TryParseOptionalDateOnly(departureFrom, out var departureFromDate))
        {
            return Results.BadRequest(new { message = "departureFrom phải là ngày, định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        if (!TryParseOptionalDateOnly(departureTo, out var departureToDate))
        {
            return Results.BadRequest(new { message = "departureTo phải là ngày, định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        return Results.Ok(await sender.Send(
            new GetBookingManagementListQuery(
                keyword,
                bookingStatus,
                paymentStatus,
                serviceType,
                paymentMethod,
                soldByStaffId,
                createdFromDate.HasValue ? ToVietnamStartOfDay(createdFromDate.Value) : null,
                createdToDate.HasValue ? ToVietnamStartOfDay(createdToDate.Value.AddDays(1)) : null,
                departureFromDate.HasValue ? ToVietnamStartOfDay(departureFromDate.Value) : null,
                departureToDate.HasValue ? ToVietnamStartOfDay(departureToDate.Value.AddDays(1)) : null,
                page <= 0 ? 1 : page,
                pageSize <= 0 ? 20 : pageSize),
            cancellationToken));
    }

    private static async Task<IResult> GetBookingSelectOptions(
        ISender sender,
        [FromQuery] string? keyword,
        [FromQuery] string? bookingStatus,
        [FromQuery] string? paymentStatus,
        [FromQuery] string? serviceType,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new GetBookingSelectOptionsQuery(
                keyword,
                bookingStatus,
                paymentStatus,
                serviceType,
                limit <= 0 ? 20 : limit),
            cancellationToken));

    private static bool TryParseOptionalDateOnly(string? value, out DateOnly? date)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            date = null;
            return true;
        }

        if (DateOnly.TryParseExact(
                value.Trim(),
                ["yyyy-MM-dd", "dd/MM/yyyy", "dd-MM-yyyy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            date = parsed;
            return true;
        }

        date = null;
        return false;
    }

    private static DateTimeOffset ToVietnamStartOfDay(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), VietnamOffset);
}
