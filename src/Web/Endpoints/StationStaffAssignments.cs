using SaigonWaterbus.Application.StationStaffAssignments;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class StationStaffAssignments : IEndpointGroup
{
    public static string RoutePrefix => "/api/station-staff-assignments";
    public static string OpenApiTag => "StationStaffAssignments";

    private const string AssignExample =
        """
        {
          "stationId": "00000000-0000-0000-0000-000000000001",
          "staffUserId": "00000000-0000-0000-0000-000000000010",
          "sourceType": "CharterBooking",
          "sourceId": "00000000-0000-0000-0000-000000000020",
          "workingDate": "2026-08-01",
          "shiftCode": "Day",
          "dutyRole": "CheckIn"
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetStationStaffAssignments, string.Empty)
            .RequireAuthorization()
            .WithSummary("Danh sach phan cong nhan vien mat dat")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Query params optional: sourceType=RegularTrip|CharterBooking, sourceId, stationId, workingDate=yyyy-MM-dd, activeOnly=true|false.",
                "Admin xem tat ca.",
                "Manager chi xem cac phan cong tai ben minh phu trach.",
                "Staff chi xem phan cong cua chinh minh."));

        group.MapPost(AssignStationStaff, string.Empty)
            .RequireAuthorization()
            .WithSummary("Phan cong nhan vien mat dat cho chuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager bến",
                AssignExample,
                "sourceType hop le: RegularTrip | CharterBooking.",
                "Staff duoc chon phai role Staff, staffType=Ground, Active va dang thuoc stationId.",
                "Manager chi phan cong duoc tai cac ben minh phu trach.",
                "shiftCode optional: Day hoặc Evening; mac dinh Day.",
                "dutyRole goi y: CheckIn, Boarding, PierSupport."));

        group.MapDelete(DeactivateStationStaffAssignment, "{assignmentId:guid}")
            .RequireAuthorization()
            .WithSummary("Huy phan cong nhan vien mat dat")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager bến",
                null,
                "Soft deactivate: dat isActive=false.",
                "Manager chi huy duoc phan cong tai ben minh phu trach."));
    }

    private static async Task<IResult> GetStationStaffAssignments(
        ISender sender,
        OperationScheduleSourceType? sourceType,
        Guid? sourceId,
        Guid? stationId,
        DateOnly? workingDate,
        bool? activeOnly,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetStationStaffAssignmentsQuery(
            sourceType,
            sourceId,
            stationId,
            workingDate,
            activeOnly ?? true), ct));

    private static async Task<IResult> AssignStationStaff(
        ISender sender,
        AssignStationStaffRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new AssignStationStaffCommand(
            request.StationId,
            request.StaffUserId,
            request.SourceType,
            request.SourceId,
            request.WorkingDate,
            request.ShiftCode,
            request.DutyRole), ct));

    private static async Task<IResult> DeactivateStationStaffAssignment(
        ISender sender,
        Guid assignmentId,
        CancellationToken ct)
    {
        await sender.Send(new DeactivateStationStaffAssignmentCommand(assignmentId), ct);
        return Results.NoContent();
    }

    public sealed record AssignStationStaffRequest(
        Guid StationId,
        Guid StaffUserId,
        OperationScheduleSourceType SourceType,
        Guid SourceId,
        DateOnly WorkingDate,
        string? ShiftCode = null,
        string? DutyRole = null);
}
