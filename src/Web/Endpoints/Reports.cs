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
                "paymentMethod optional: Cash | PayOs | Free.",
                "fromStationId / toStationId: lọc theo bến đi / bến đến.",
                "soldByStaffId: lọc theo nhân viên bán.",
                "Response có byStation để xem doanh thu theo từng bến (departure/arrival).",
                "Doanh thu ròng = tổng payment đã thu - refundAmount."));

        group.MapGet(GetWaterbusStationRevenue, "revenue/waterbus/stations")
            .RequireAuthorization()
            .WithSummary("Doanh thu waterbus theo ben (di va den)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager",
                null,
                "Thong ke doanh thu chi danh cho Waterbus (BookingType=SeatBooking + RouteType=Waterbus).",
                "fromDate/toDate dang yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy; toDate là ngày cuối được tính.",
                "Mặc định: đầu tháng hiện tại → hôm nay theo giờ Việt Nam.",
                "Mỗi bến tách departure (đi) và arrival (đến): số chuyến, vé, gross, refund, net.",
                "Chỉ tính Payments đã Paid qua PayOs / Counter / Free."));

        group.MapGet(GetTopCustomers, "revenue/top-customers")
            .RequireAuthorization()
            .WithSummary("Top khach dat nhieu nhat (gop ca online va quay)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager",
                null,
                "Thong ke top khach hang theo tong tien da thanh toan trong khoang fromDate/toDate.",
                "Gop ca booking online (co UserId) lan booking mua tai quay (co SoldByStaffId), tu khoa nhan dang theo email > phone > name.",
                "fromDate/toDate dang yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy; toDate là ngày cuối được tính.",
                "serviceType optional: Waterbus | Sightseeing | Charter.",
                "paymentMethod optional: Cash | PayOs | Free.",
                "limit mac dinh 10, toi da 50."));

        group.MapGet(GetReportBookings, "bookings")
            .RequireAuthorization()
            .WithSummary("Tong hop / quan ly booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Staff chỉ thấy booking bán tại quầy của chính mình; Admin/Manager thấy toàn bộ.",
                "keyword tìm theo bookingCode/contactName/contactPhone/contactEmail.",
                "bookingStatus optional: PendingPayment | Confirmed | Cancelled | Expired | Refunded | Completed | ...",
                "paymentStatus optional: Unpaid | DepositPaid | Paid | Refunded | Failed.",
                "serviceType optional: Waterbus | Sightseeing | Charter.",
                "paymentMethod optional: Cash | PayOs | Free.",
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

        group.MapGet(ExportBookingReportExcel, "bookings/export")
            .RequireAuthorization()
            .WithSummary("Xuat file Excel danh sach booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Xuất Excel tổng hợp booking theo bộ lọc tương tự /reports/bookings.",
                "Staff chỉ thấy booking bán tại quầy của chính mình.",
                "Query: keyword, bookingStatus, paymentStatus, serviceType, paymentMethod, soldByStaffId, createdFrom, createdTo, departureFrom, departureTo.",
                "Response: file Excel (.xlsx)."));
    }

    private static async Task<IResult> GetRevenue(
        ISender sender,
        [FromQuery] string? fromDate,
        [FromQuery] string? toDate,
        [FromQuery] string? serviceType,
        [FromQuery] string? paymentMethod,
        [FromQuery] Guid? soldByStaffId,
        [FromQuery] Guid? fromStationId,
        [FromQuery] Guid? toStationId,
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
                soldByStaffId,
                fromStationId,
                toStationId),
            cancellationToken));
    }

    private static async Task<IResult> GetWaterbusStationRevenue(
        ISender sender,
        [FromQuery] string? fromDate,
        [FromQuery] string? toDate,
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
            new GetWaterbusStationRevenueQuery(
                ToVietnamStartOfDay(startDate),
                ToVietnamStartOfDay(endDate.AddDays(1))),
            cancellationToken));
    }

    private static async Task<IResult> GetTopCustomers(
        ISender sender,
        [FromQuery] string? fromDate,
        [FromQuery] string? toDate,
        [FromQuery] string? serviceType,
        [FromQuery] string? paymentMethod,
        [FromQuery] int? limit,
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
            new GetTopCustomersQuery(
                ToVietnamStartOfDay(startDate),
                ToVietnamStartOfDay(endDate.AddDays(1)),
                serviceType,
                paymentMethod,
                limit is null or <= 0 ? 10 : limit.Value),
            cancellationToken));
    }

    private static async Task<IResult> GetReportBookings(
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

    private static async Task<IResult> ExportBookingReportExcel(
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

        var excelBytes = await sender.Send(
            new ExportBookingReportExcelQuery(
                keyword,
                bookingStatus,
                paymentStatus,
                serviceType,
                paymentMethod,
                soldByStaffId,
                createdFromDate.HasValue ? ToVietnamStartOfDay(createdFromDate.Value) : null,
                createdToDate.HasValue ? ToVietnamStartOfDay(createdToDate.Value.AddDays(1)) : null,
                departureFromDate.HasValue ? ToVietnamStartOfDay(departureFromDate.Value) : null,
                departureToDate.HasValue ? ToVietnamStartOfDay(departureToDate.Value.AddDays(1)) : null),
            cancellationToken);

        var fileName = $"booking_report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
        return Results.File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

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
