using System.Globalization;
using SaigonWaterbus.Application.Operations;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Operations : IEndpointGroup
{
    private const string DelayScheduleExample =
        """
        {
          "delayMinutes": 30,
          "reason": "Mưa lớn, tàu khởi hành trễ."
        }
        """;

    private const string RefreshScheduleExample =
        """
        {
          "from": "2026-06-20",
          "to": "2026-06-21"
        }
        """;

    public static string RoutePrefix => "/api/operations";

    public static string OpenApiTag => "Operations";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetSchedule, "schedule")
            .RequireAuthorization()
            .WithSummary("Xem lịch vận hành chung")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager, Staff hoặc Customer",
                null,
                "Query params: from, to dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy. Nếu bỏ to thì lấy một ngày.",
                "GET chỉ đọc dữ liệu đã được đồng bộ bởi job nền hoặc POST /api/operations/schedule/sync.",
                "Admin/Manager/Staff xem lịch nội bộ. Customer xem lịch tuyến thường và custom booking của chính họ."));

        groupBuilder.MapPost(RefreshSchedule, "schedule/sync")
            .RequireAuthorization()
            .WithSummary("Đồng bộ ngay lịch vận hành")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                RefreshScheduleExample,
                "Đồng bộ bảng operation_schedule_entries từ trips và custom_booking_requests trong khoảng ngày yêu cầu.",
                "Nếu bỏ from thì mặc định hôm nay theo giờ Việt Nam; nếu bỏ to thì lấy cùng ngày với from."));

        groupBuilder.MapPatch(DelayScheduleEntry, "schedule/{id:guid}/delay")
            .RequireAuthorization()
            .WithSummary("Cập nhật delay cho lịch vận hành")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                DelayScheduleExample,
                "Không sửa giờ gốc startAt/endAt.",
                "Backend lưu delayMinutes, delayReason, adjustedStartAt/adjustedEndAt và operationStatus=Delayed.",
                "Gọi GET /api/operations/schedule để xem lại lịch đã delay."));
    }

    private static async Task<IResult> GetSchedule(
        ISender sender,
        string? from,
        string? to,
        bool includeCancelled,
        CancellationToken cancellationToken)
    {
        if (!TryParseDateOnly(from, out var fromDate))
        {
            return Results.BadRequest(new { message = "from phải có định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        if (!TryParseOptionalDateOnly(to, out var toDate))
        {
            return Results.BadRequest(new { message = "to phải có định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        var startDate = fromDate ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var endDate = toDate ?? startDate;
        if (endDate < startDate)
        {
            return Results.BadRequest(new { message = "to phải lớn hơn hoặc bằng from." });
        }

        if (endDate.DayNumber - startDate.DayNumber > 62)
        {
            return Results.BadRequest(new { message = "Khoảng xem lịch không được vượt quá 62 ngày." });
        }

        var fromAt = ToVietnamStartOfDay(startDate);
        var toAt = ToVietnamStartOfDay(endDate.AddDays(1));
        return Results.Ok(await sender.Send(
            new GetOperationScheduleQuery(fromAt, toAt, includeCancelled),
            cancellationToken));
    }

    private static async Task<IResult> RefreshSchedule(
        ISender sender,
        RefreshOperationScheduleApiRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParseDateOnly(request.From, out var fromDate))
        {
            return Results.BadRequest(new { message = "from phải có định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        if (!TryParseOptionalDateOnly(request.To, out var toDate))
        {
            return Results.BadRequest(new { message = "to phải có định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        var startDate = fromDate ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var endDate = toDate ?? startDate;
        if (endDate < startDate)
        {
            return Results.BadRequest(new { message = "to phải lớn hơn hoặc bằng from." });
        }

        if (endDate.DayNumber - startDate.DayNumber > 62)
        {
            return Results.BadRequest(new { message = "Khoảng đồng bộ lịch không được vượt quá 62 ngày." });
        }

        var fromAt = ToVietnamStartOfDay(startDate);
        var toAt = ToVietnamStartOfDay(endDate.AddDays(1));
        return Results.Ok(await sender.Send(
            new RefreshOperationScheduleCommand(fromAt, toAt),
            cancellationToken));
    }

    private static async Task<IResult> DelayScheduleEntry(
        ISender sender,
        Guid id,
        DelayOperationScheduleEntryApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new DelayOperationScheduleEntryCommand(
                id,
                request.DelayMinutes,
                request.Reason),
            cancellationToken));

    private static DateTimeOffset ToVietnamStartOfDay(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7));

    private static bool TryParseOptionalDateOnly(string? value, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (TryParseDate(value, out var parsed))
        {
            date = parsed;
            return true;
        }

        return false;
    }

    private static bool TryParseDateOnly(string? value, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (TryParseDate(value, out var parsed))
        {
            date = parsed;
            return true;
        }

        return false;
    }

    private static bool TryParseDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value,
            ["yyyy-MM-dd", "dd/MM/yyyy", "dd-MM-yyyy"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    public sealed record DelayOperationScheduleEntryApiRequest(
        int DelayMinutes,
        string Reason);

    public sealed record RefreshOperationScheduleApiRequest(
        string? From,
        string? To);
}
