using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using SaigonWaterbus.Application.Operations;
using SaigonWaterbus.Domain.Constants;

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
          "fromDate": "2026-06-20",
          "toDate": "2026-06-21"
        }
        """;

    public static string RoutePrefix => "/api/operations";

    public static string OpenApiTag => "Operations";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetSchedule, "schedule")
            .AllowAnonymous()
            .WithSummary("Xem lịch vận hành chung")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous, Customer, Admin, Manager hoặc Staff",
                null,
                "Query: fromDate, toDate dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy; bỏ toDate thì lấy một ngày.",
                "serviceType optional: booking | bus | sightseeing | charter | all.",
                "Anonymous/Customer xem tối đa 7 ngày và chỉ thấy Bus + Sightseeing.",
                "Response chính: operatingDate, fromLocation, toLocation, scheduledDepartureAt, endAt, stops[]."));

        groupBuilder.MapPost(RefreshSchedule, "schedule/sync")
            .RequireAuthorization()
            .WithSummary("Đồng bộ ngay lịch vận hành")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                RefreshScheduleExample,
                "Body dùng fromDate, toDate là ngày bắt đầu/kết thúc, không phải bến đi/bến đến.",
                "Đồng bộ bảng operation_schedule_entries từ trips và bookings trong khoảng ngày yêu cầu.",
                "Nếu bỏ fromDate thì mặc định hôm nay theo giờ Việt Nam; nếu bỏ toDate thì lấy cùng ngày với fromDate.",
                "Field cũ from/to vẫn được đọc để tương thích, nhưng FE nên dùng fromDate/toDate."));

        groupBuilder.MapPatch(DelayScheduleEntry, "schedule/{id:guid}/delay")
            .RequireAuthorization()
            .WithSummary("Cập nhật delay cho lịch vận hành")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                DelayScheduleExample,
                "Flow cũ của operation_schedule_entries đã bỏ. FE KHONG dung endpoint nay de bao delay.",
                "Delay dung API trip: POST /api/trips/{id}/delay/start va POST /api/trips/{id}/delay/resume.",
                "Sau khi resume, BE tu cap nhat adjusted time, day chuyen cac trip sau va gui notification cho customer."));
    }

    private static async Task<IResult> GetSchedule(
        ISender sender,
        HttpRequest httpRequest,
        [FromQuery] string? fromDate,
        [FromQuery] string? toDate,
        [FromQuery] bool includeCancelled,
        [FromQuery] string? serviceType,
        [FromQuery] string? routeType,
        [FromQuery] Guid? stationId,
        CancellationToken cancellationToken)
    {
        var requestedFromDate = fromDate ?? GetQueryValue(httpRequest, "from");
        var requestedToDate = toDate ?? GetQueryValue(httpRequest, "to");

        if (!TryParseDateOnly(requestedFromDate, out var parsedFromDate))
        {
            return Results.BadRequest(new { message = "fromDate phải là ngày, định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        if (!TryParseOptionalDateOnly(requestedToDate, out var parsedToDate))
        {
            return Results.BadRequest(new { message = "toDate phải là ngày, định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        var startDate = parsedFromDate ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var endDate = parsedToDate ?? startDate;
        if (endDate < startDate)
        {
            return Results.BadRequest(new { message = "toDate phải lớn hơn hoặc bằng fromDate." });
        }

        if (endDate.DayNumber - startDate.DayNumber > 62)
        {
            return Results.BadRequest(new { message = "Khoảng xem lịch không được vượt quá 62 ngày." });
        }

        var fromAt = ToVietnamStartOfDay(startDate);
        var toAt = ToVietnamStartOfDay(endDate.AddDays(1));
        return Results.Ok(await sender.Send(
            new GetOperationScheduleQuery(fromAt, toAt, includeCancelled, serviceType, routeType, stationId),
            cancellationToken));
    }

    private static async Task<IResult> RefreshSchedule(
        ISender sender,
        RefreshOperationScheduleApiRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParseDateOnly(request.ResolveFromDate(), out var fromDate))
        {
            return Results.BadRequest(new { message = "fromDate phải là ngày, định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        if (!TryParseOptionalDateOnly(request.ResolveToDate(), out var toDate))
        {
            return Results.BadRequest(new { message = "toDate phải là ngày, định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        var startDate = fromDate ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var endDate = toDate ?? startDate;
        if (endDate < startDate)
        {
            return Results.BadRequest(new { message = "toDate phải lớn hơn hoặc bằng fromDate." });
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

    private static string? GetQueryValue(HttpRequest request, string name)
    {
        var value = request.Query[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

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

    public sealed record RefreshOperationScheduleApiRequest
    {
        public string? FromDate { get; init; }

        public string? ToDate { get; init; }

        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; init; }

        public string? ResolveFromDate() => FromDate ?? GetLegacyDate("from");

        public string? ResolveToDate() => ToDate ?? GetLegacyDate("to");

        private string? GetLegacyDate(string name)
        {
            if (ExtensionData is null
                || !ExtensionData.TryGetValue(name, out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var date = value.GetString();
            return string.IsNullOrWhiteSpace(date) ? null : date;
        }
    }
}
