using System.Globalization;
using SaigonWaterbus.Application.StaffWorkAssignments;
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
          "stationId": "00000000-0000-0000-0000-000000000001",
          "startAt": "2026-07-13T08:00:00+07:00",
          "endAt": "2026-07-13T16:00:00+07:00",
          "dutyRole": "Gate",
          "note": "Ca sáng"
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
                "Query optional: fromDate, toDate, staffUserId, assignmentType, boatId, stationId, status.",
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
                "Boat/Station: bắt buộc startAt và endAt.",
                "Backend chặn staff bị trùng ca."));

        group.MapPatch(UpdateStaffWorkAssignmentStatus, "{assignmentId:guid}/status")
            .RequireAuthorization()
            .WithSummary("Cập nhật trạng thái phân công")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager",
                """{ "status": "Cancelled" }""",
                "status: Scheduled | Active | Completed | Cancelled.",
                "Manager chỉ cập nhật phân công Station trong bến mình phụ trách."));

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
                request.StartAt,
                request.EndAt,
                request.DutyRole,
                request.Note),
            cancellationToken));

    private static async Task<IResult> UpdateStaffWorkAssignmentStatus(
        ISender sender,
        Guid assignmentId,
        UpdateStaffWorkAssignmentStatusRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new UpdateStaffWorkAssignmentStatusCommand(assignmentId, request.Status),
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
        DateTimeOffset? StartAt = null,
        DateTimeOffset? EndAt = null,
        string? DutyRole = null,
        string? Note = null);

    public sealed record UpdateStaffWorkAssignmentStatusRequest(StaffWorkAssignmentStatus Status);
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
