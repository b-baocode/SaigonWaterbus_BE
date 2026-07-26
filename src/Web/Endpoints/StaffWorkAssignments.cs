using System.Globalization;
using SaigonWaterbus.Application.StaffWorkAssignments;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class StaffWorkAssignments : IEndpointGroup
{
    public static string RoutePrefix => "/api/staff-assignments";

    public static string OpenApiTag => "StaffAssignments";

    private const string CreateExample =
        """
        {
          "staffUserId": "00000000-0000-0000-0000-000000000000",
          "assignmentType": "Station",
          "tripStopId": "00000000-0000-0000-0000-000000000004",
          "stationId": "00000000-0000-0000-0000-000000000001",
          "startAt": "2026-07-13T08:00:00+07:00",
          "endAt": "2026-07-13T16:00:00+07:00",
          "dutyRole": "Gate"
        }
        """;

    private const string CreateBulkExample =
        """
        {
          "staffUserId": "00000000-0000-0000-0000-000000000000",
          "assignmentType": "Boat",
          "boatId": "00000000-0000-0000-0000-000000000002",
          "fromDate": "2026-07-01",
          "toDate": "2026-07-31",
          "startTime": "07:30:00",
          "endTime": "15:00:00",
          "daysOfWeek": [1, 2, 3, 4, 5],
          "dutyRole": "OnBoard"
        }
        """;

    private const string ReplaceExample =
        """
        {
          "replacementStaffUserId": "00000000-0000-0000-0000-000000000003",
          "reason": "Nhân viên hiện tại nghỉ đột xuất"
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(ListStaffWorkAssignments, string.Empty)
            .RequireAuthorization()
            .WithSummary("Danh sách phân công ca làm staff")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager",
                null,
                "Query optional: fromDate, toDate, staffUserId, assignmentType, boatId, stationId, tripStopId, status.",
                "assignmentType: Boat | Station.",
                "Admin xem tất cả. Manager chỉ xem phân công Station trong các bến mình phụ trách.",
                "Dùng cho màn quản lý phân công nhân viên."));

        group.MapPost(CreateStaffWorkAssignment, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tạo phân công ca làm staff")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager",
                CreateExample,
                "Admin gán staff OnBoard vào Boat.",
                "Manager gán staff Ground vào Station trong bến mình phụ trách.",
                "Boat/Station: bắt buộc startAt và endAt. Muốn tạo trip cho một tàu thì phải có ít nhất 2 ca Boat của staff OnBoard phủ toàn bộ thời gian chuyến.",
                "Staff check/scan vé lấy từ ca Boat trên tàu; không cần gán tripStop riêng cho nhân viên check vé.",
                "Một ca lẻ tối đa 24 giờ. Nếu cần tạo lịch nhiều ngày/tháng, dùng POST /api/staff-assignments/bulk.",
                "Backend chặn staff bị trùng ca."));

        group.MapPost(CreateBulkStaffWorkAssignments, "bulk")
            .RequireAuthorization()
            .WithSummary("Tạo lịch phân ca lặp nhiều ngày")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager",
                CreateBulkExample,
                "BE sinh từng bản ghi ca làm theo từng ngày trong khoảng fromDate/toDate.",
                "daysOfWeek dùng chuẩn ISO: 1 = Thứ 2, 2 = Thứ 3, ..., 7 = Chủ nhật. Bỏ trống/null nghĩa là tạo tất cả các ngày.",
                "Admin gán staff OnBoard vào Boat. Manager chỉ gán staff Ground vào Station thuộc bến mình phụ trách.",
                "Để tạo/generate trip, FE/admin cần tạo đủ 2 staff OnBoard cho cùng boat và khung giờ phủ chuyến.",
                "startTime/endTime là giờ Việt Nam; endTime nhỏ hơn hoặc bằng startTime nghĩa là ca qua đêm.",
                "Mỗi ca sinh ra vẫn tối đa 24 giờ và BE chặn trùng ca."));

        group.MapPost(ReplaceStaffWorkAssignment, "{assignmentId:guid}/replace")
            .RequireAuthorization()
            .WithSummary("Thay nhân viên của một ca làm")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager",
                ReplaceExample,
                "BE đổi ca cũ sang status = Replaced và tạo ca mới cùng thời gian/cùng Boat hoặc Station cho nhân viên thay thế.",
                "Dùng khi nhân viên tàu hoặc nhân viên bến đổi ca, nghỉ đột xuất, hoặc cần chuyển giao.",
                "Ca đã Replaced không còn hiện trong lịch mobile/chuyến của nhân viên cũ."));

        group.MapDelete(DeleteStaffWorkAssignment, "{assignmentId:guid}")
            .RequireAuthorization()
            .WithSummary("Hủy phân công ca làm")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager",
                null,
                "Soft delete: backend đổi status = Cancelled.",
                "Manager chỉ hủy phân công Station trong bến mình phụ trách."));
    }

    private static async Task<IResult> ListStaffWorkAssignments(
        ISender sender,
        string? fromDate,
        string? toDate,
        Guid? staffUserId,
        StaffWorkAssignmentType? assignmentType,
        Guid? boatId,
        Guid? stationId,
        Guid? tripStopId,
        StaffWorkAssignmentStatus? status,
        CancellationToken cancellationToken)
    {
        if (!TryParseOptionalDateOnly(fromDate, out var parsedFromDate))
        {
            return Results.BadRequest(new { message = "fromDate phải có định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        if (!TryParseOptionalDateOnly(toDate, out var parsedToDate))
        {
            return Results.BadRequest(new { message = "toDate phải có định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        return Results.Ok(await sender.Send(
            new GetStaffWorkAssignmentsQuery(
                parsedFromDate,
                parsedToDate,
                staffUserId,
                assignmentType,
                boatId,
                stationId,
                tripStopId,
                status),
            cancellationToken));
    }

    private static async Task<IResult> CreateStaffWorkAssignment(
        ISender sender,
        CreateStaffWorkAssignmentRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new CreateStaffWorkAssignmentCommand(
                request.StaffUserId,
                request.AssignmentType,
                request.BoatId,
                request.StationId,
                request.TripStopId,
                request.StartAt,
                request.EndAt,
                request.DutyRole),
            cancellationToken));

    private static async Task<IResult> CreateBulkStaffWorkAssignments(
        ISender sender,
        CreateBulkStaffWorkAssignmentsRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new CreateBulkStaffWorkAssignmentsCommand(
                request.StaffUserId,
                request.AssignmentType,
                request.BoatId,
                request.StationId,
                request.FromDate,
                request.ToDate,
                request.StartTime,
                request.EndTime,
                request.DaysOfWeek,
                request.DutyRole),
            cancellationToken));

    private static async Task<IResult> ReplaceStaffWorkAssignment(
        ISender sender,
        Guid assignmentId,
        ReplaceStaffWorkAssignmentRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new ReplaceStaffWorkAssignmentCommand(
                assignmentId,
                request.ReplacementStaffUserId,
                request.Reason),
            cancellationToken));

    private static async Task<IResult> DeleteStaffWorkAssignment(
        ISender sender,
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteStaffWorkAssignmentCommand(assignmentId), cancellationToken);
        return Results.NoContent();
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

    internal static bool TryParseDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value,
            ["yyyy-MM-dd", "dd/MM/yyyy", "dd-MM-yyyy"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    public sealed record CreateStaffWorkAssignmentRequest(
        Guid StaffUserId,
        StaffWorkAssignmentType AssignmentType,
        Guid? BoatId = null,
        Guid? StationId = null,
        Guid? TripStopId = null,
        DateTimeOffset? StartAt = null,
        DateTimeOffset? EndAt = null,
        string? DutyRole = null);

    public sealed record CreateBulkStaffWorkAssignmentsRequest(
        Guid StaffUserId,
        StaffWorkAssignmentType AssignmentType,
        Guid? BoatId,
        Guid? StationId,
        DateOnly FromDate,
        DateOnly ToDate,
        TimeOnly StartTime,
        TimeOnly EndTime,
        IReadOnlyCollection<int>? DaysOfWeek = null,
        string? DutyRole = null);

    public sealed record ReplaceStaffWorkAssignmentRequest(
        Guid ReplacementStaffUserId,
        string? Reason = null);

}

public sealed class Staff : IEndpointGroup
{
    public static string RoutePrefix => "/api/staff";

    public static string OpenApiTag => "Staff";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetMyAssignments, "me/assignments")
            .RequireAuthorization()
            .WithSummary("Mobile staff xem lịch làm")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Staff",
                null,
                "Query optional: fromDate, toDate. Nếu bỏ trống thì lấy hôm nay theo giờ Việt Nam.",
                "Chỉ trả phân công của staff đang đăng nhập.",
                "shiftState: Upcoming | Active | Completed | Cancelled.",
                "assignmentType: Boat | Station."));

        group.MapGet(GetMyTodayAssignments, "me/today")
            .RequireAuthorization()
            .WithSummary("Mobile staff xem lịch hôm nay")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Staff",
                null,
                "Shortcut của GET /api/staff/me/assignments cho ngày hiện tại."));

        group.MapGet(GetMyTrips, "me/trips")
            .RequireAuthorization()
            .WithSummary("Mobile staff xem chuyến được xử lý theo ca")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Staff",
                null,
                "Query optional: date=yyyy-MM-dd/dd-MM-yyyy/dd/MM/yyyy. Nếu bỏ trống thì lấy hôm nay theo giờ Việt Nam.",
                "BE tự suy ra trip từ phân công Boat hoặc Station của staff.",
                "Boat: trip có boatId trùng và thời gian trip overlap ca.",
                "Station: trip có route đi qua station và thời gian trip overlap ca."));

        group.MapGet(GetMyScanHistory, "me/scan-history")
            .RequireAuthorization()
            .WithSummary("Mobile staff xem lịch sử quét vé của mình")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Staff",
                null,
                "Query optional: fromDate, toDate, tripId, action=Scan|CheckIn|CheckOut, result=Success|Failed, source=Qr|Manual|Override.",
                "Nếu bỏ fromDate/toDate thì lấy hôm nay theo giờ Việt Nam.",
                "Chỉ trả các event do staff đang đăng nhập thực hiện."));

        group.MapGet(GetMyCurrentShift, "me/current-shift")
            .RequireAuthorization()
            .WithSummary("Mobile staff xem ca hiện tại")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Staff",
                null,
                "Trả currentShift nếu có ca đang Active theo giờ hiện tại.",
                "todayAssignments trả toàn bộ ca hôm nay để mobile hiển thị upcoming/completed."));
    }

    private static async Task<IResult> GetMyAssignments(
        ISender sender,
        string? fromDate,
        string? toDate,
        CancellationToken cancellationToken)
    {
        if (!ResolveDateRange(fromDate, toDate, out var from, out var to, out var error))
        {
            return Results.BadRequest(new { message = error });
        }

        return Results.Ok(await sender.Send(
            new GetMyStaffWorkAssignmentsQuery(from, to),
            cancellationToken));
    }

    private static async Task<IResult> GetMyTodayAssignments(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var today = TodayInVietnam();
        return Results.Ok(await sender.Send(
            new GetMyStaffWorkAssignmentsQuery(today, today),
            cancellationToken));
    }

    private static async Task<IResult> GetMyTrips(
        ISender sender,
        string? date,
        CancellationToken cancellationToken)
    {
        var requestedDate = TodayInVietnam();
        if (!string.IsNullOrWhiteSpace(date)
            && !StaffWorkAssignments.TryParseDate(date, out requestedDate))
        {
            return Results.BadRequest(new { message = "date phải có định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy." });
        }

        return Results.Ok(await sender.Send(
            new GetMyStaffTripsQuery(requestedDate),
            cancellationToken));
    }

    private static async Task<IResult> GetMyScanHistory(
        ISender sender,
        string? fromDate,
        string? toDate,
        Guid? tripId,
        TicketScanAction? action,
        TicketScanResult? result,
        TicketScanSource? source,
        CancellationToken cancellationToken)
    {
        if (!ResolveDateRange(fromDate, toDate, out var from, out var to, out var error))
        {
            return Results.BadRequest(new { message = error });
        }

        return Results.Ok(await sender.Send(
            new GetMyTicketScanHistoryQuery(from, to, tripId, action, result, source),
            cancellationToken));
    }

    private static async Task<IResult> GetMyCurrentShift(
        ISender sender,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new GetMyCurrentStaffShiftQuery(), cancellationToken));

    private static bool ResolveDateRange(
        string? fromDate,
        string? toDate,
        out DateOnly from,
        out DateOnly to,
        out string error)
    {
        error = string.Empty;
        from = TodayInVietnam();
        to = from;

        if (!string.IsNullOrWhiteSpace(fromDate)
            && !StaffWorkAssignments.TryParseDate(fromDate, out from))
        {
            error = "fromDate phải có định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(toDate)
            && !StaffWorkAssignments.TryParseDate(toDate, out to))
        {
            error = "toDate phải có định dạng yyyy-MM-dd, dd/MM/yyyy hoặc dd-MM-yyyy.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(toDate))
        {
            to = from;
        }

        if (to < from)
        {
            error = "toDate phải lớn hơn hoặc bằng fromDate.";
            return false;
        }

        return true;
    }

    private static DateOnly TodayInVietnam() =>
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
}
