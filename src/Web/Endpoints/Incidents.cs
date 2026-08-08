using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Incidents;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Options;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Incidents : IEndpointGroup
{
    private const string LiveHookSecretHeaderName = "X-Live-Hook-Secret";

    public static string RoutePrefix => "/api/incidents";

    private const string CreateIncidentExample =
        """
        {
          "boatId": "00000000-0000-0000-0000-000000000000",
          "tripId": null,
          "incidentType": "MechanicalFailure",
          "severity": "High",
          "description": "Tau bi hong dong co tai ben.",
          "occurredAt": "2026-06-28T10:00:00+07:00"
        }
        """;

    private const string ResolveIncidentExample =
        """
        {
          "resolutionNote": "Da dieu tau thay the va dua tau cu ve bao tri.",
          "boatStatus": "UnderMaintenance",
          "tripStatus": "Cancelled"
        }
        """;

    private const string AssignReplacementBoatExample =
        """
        {
          "rescueBoatId": "00000000-0000-0000-0000-000000000000",
          "replacementBoatId": "00000000-0000-0000-0000-000000000000",
          "delayMinutes": 30,
          "note": "Dieu tau cuu ho va tau thay the ho tro khach tai vi tri su co."
        }
        """;

    private const string CompleteRescueMissionExample =
        """
        {
          "incidentId": "00000000-0000-0000-0000-000000000000",
          "boatCode": "WB_005",
          "rescueBoatCode": "SOS_001",
          "completedAt": "2026-07-18T10:30:00+07:00",
          "note": "Tau da duoc keo ve ben."
        }
        """;

    private const string GpsEventExample =
        """
        {
          "gpsEventId": "uuid-from-gps",
          "event": "RescueArrived",
          "boatCode": "RESCUE_001",
          "occurredAt": "2026-08-07T10:30:00+07:00",
          "lat": 10.8,
          "lng": 106.7,
          "stationId": null,
          "stationCode": null,
          "note": "Tau cuu ho da toi vi tri su co.",
          "previousMissionStatus": "Dispatched",
          "estimatedTowingMinutes": null
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetIncidentList, string.Empty)
            .RequireAuthorization()
            .WithSummary("Danh sach su co tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Staff",
                null,
                "Query params optional: boatId, tripId, resolutionStatus.",
                "Dung de xem cac su co dang Open hoac da Resolved."));

        group.MapPost(CreateIncident, string.Empty)
            .RequireAuthorization()
            .WithSummary("Bao su co tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Staff",
                CreateIncidentExample,
                "boatId bat buoc. tripId optional neu su co xay ra trong mot chuyen cu the.",
                "Khi tao su co, tau duoc chuyen sang Incident.",
                "Neu co tripId: trip chuyen sang Delayed de cho Admin dieu tau va nhap delay; BE khong tu Cancelled theo severity."));

        group.MapPatch(AssignReplacementBoat, "{incidentId:guid}/assign-replacement-boat")
            .RequireAuthorization()
            .WithSummary("Gan tau thay the/cuu ho cho su co")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                AssignReplacementBoatExample,
                "rescueBoatId bat buoc, phai la tau serviceType Rescue dang Active.",
                "BE tinh khach bi anh huong theo vi tri tau: onboardPassengerCount va futurePassengerCount.",
                "Neu co khach dang tren tau: replacementBoatId bat buoc, mission TransferAtIncidentLocation.",
                "Neu chua co khach tren tau nhung co khach cho o ben sau: replacementBoatId bat buoc, mission ContinueFromStation va co replacementTargetStation.",
                "Neu khong co khach bi anh huong: co the chi gui rescueBoatId, replacementBoatId de null, mission None.",
                "Neu khong co khach bi anh huong nhung Admin van muon tiep tuc chuyen: co the gui them replacementBoatId de doi tau cua trip.",
                "Neu su co khong co tripId: chi gui rescueBoatId; replacementBoatId phai de null.",
                "Tau thay the phai serviceType Passenger, Active, setup du ghe va khong trung tau cuu ho/tau gap su co.",
                "delayMinutes la so phut Admin nhap luc dieu tau; vi du 5 thi trip cong 5 phut, TripStatus Delayed va adjusted time cho cac stop con lai.",
                "BE tinh day chuyen cho cac trip sau cua cung tau gap su co, cung ngay van hanh theo cong thuc: gio tau san sang = adjustedArrival chuyen truoc + 15 phut quay dau.",
                "Neu gio tau san sang lon hon gio khoi hanh du kien cua chuyen sau thi chuyen sau bi delay dung phan bi lan gio; route khac van co the bi anh huong neu cung tau."));

        group.MapPatch(ResolveIncident, "{incidentId:guid}/resolve")
            .RequireAuthorization()
            .WithSummary("Xu ly/dong su co tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                ResolveIncidentExample,
                "resolutionNote bat buoc.",
                "boatStatus/tripStatus optional de cap nhat trang thai sau khi xu ly."));

        group.MapPost(RecordGpsEvent, "{incidentId:guid}/gps-event")
            .AllowAnonymous()
            .WithSummary("GPS bao tien do cuu ho/thay the tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "GPS service",
                GpsEventExample,
                $"Header bat buoc: {LiveHookSecretHeaderName}.",
                "event hop le: RescueArrived | ReplacementArrived | PassengerTransferCompleted | TowingStarted | TowingCompleted.",
                "gpsEventId bat buoc de GPS retry khong tao trung event. Cung gpsEventId + payload khac tra 409.",
                "previousMissionStatus optional, chi dung debug; BE validate sequence theo missionStatus trong DB.",
                "ReplacementArrived voi mission ContinueFromStation phai gui stationId hoac stationCode dung replacementTargetStation.",
                "TowingStarted bi tu choi neu con khach onboard ma chua PassengerTransferCompleted."));

        group.MapPost(CompleteRescueMission, "rescue-mission-completed")
            .AllowAnonymous()
            .WithSummary("GPS callback khi tau cuu ho keo tau loi ve ben xong")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "GPS service",
                CompleteRescueMissionExample,
                $"Header bat buoc: {LiveHookSecretHeaderName}.",
                "BE verify incidentId, boatCode, rescueBoatCode truoc khi cap nhat.",
                "Khi hop le: incident Resolved, tau loi UnderMaintenance, tau cuu ho Active."));
    }

    private static async Task<IResult> GetIncidentList(
        ISender sender,
        [FromQuery] Guid? boatId,
        [FromQuery] Guid? tripId,
        [FromQuery] string? resolutionStatus,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new GetIncidentListQuery(boatId, tripId, resolutionStatus),
            cancellationToken));

    private static async Task<IResult> CreateIncident(
        ISender sender,
        CreateIncidentRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new CreateIncidentCommand(
                request.BoatId,
                request.TripId,
                request.IncidentType,
                request.Description,
                request.Severity,
                request.OccurredAt),
            cancellationToken));

    private static async Task<IResult> AssignReplacementBoat(
        ISender sender,
        Guid incidentId,
        AssignReplacementBoatRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new AssignReplacementBoatCommand(
                incidentId,
                request.RescueBoatId,
                request.ReplacementBoatId,
                request.DelayMinutes,
                request.Note),
            cancellationToken));

    private static async Task<IResult> ResolveIncident(
        ISender sender,
        Guid incidentId,
        ResolveIncidentRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new ResolveIncidentCommand(
                incidentId,
                request.ResolutionNote,
                request.BoatStatus,
                request.TripStatus),
            cancellationToken));

    private static async Task<IResult> RecordGpsEvent(
        ISender sender,
        IOptionsMonitor<IncidentGpsHookOptions> gpsHookOptions,
        Guid incidentId,
        [FromHeader(Name = LiveHookSecretHeaderName)] string? hookSecret,
        IncidentGpsEventRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValidLiveHookSecret(gpsHookOptions.CurrentValue.Secret, hookSecret))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await sender.Send(
            new RecordIncidentGpsEventCommand(
                incidentId,
                request.GpsEventId,
                request.Event,
                request.BoatCode,
                request.OccurredAt,
                request.Lat,
                request.Lng,
                request.StationId,
                request.StationCode,
                request.Note,
                request.PreviousMissionStatus,
                request.EstimatedTowingMinutes),
            cancellationToken));
    }

    private static async Task<IResult> CompleteRescueMission(
        ISender sender,
        IOptionsMonitor<IncidentGpsHookOptions> gpsHookOptions,
        [FromHeader(Name = LiveHookSecretHeaderName)] string? hookSecret,
        CompleteRescueMissionRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValidLiveHookSecret(gpsHookOptions.CurrentValue.Secret, hookSecret))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await sender.Send(
            new CompleteRescueMissionCommand(
                request.IncidentId,
                request.BoatCode,
                request.RescueBoatCode,
                request.CompletedAt,
                request.Note),
            cancellationToken));
    }

    private static bool IsValidLiveHookSecret(string? expectedSecret, string? actualSecret)
    {
        if (string.IsNullOrWhiteSpace(expectedSecret) || string.IsNullOrWhiteSpace(actualSecret))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expectedSecret.Trim());
        var actualBytes = Encoding.UTF8.GetBytes(actualSecret.Trim());
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public sealed record CreateIncidentRequest(
        Guid BoatId,
        Guid? TripId,
        string IncidentType,
        string Description,
        string? Severity,
        DateTimeOffset? OccurredAt);

    public sealed record AssignReplacementBoatRequest(
        Guid RescueBoatId,
        Guid? ReplacementBoatId,
        int? DelayMinutes,
        string? Note);

    public sealed record ResolveIncidentRequest(
        string ResolutionNote,
        BoatStatus? BoatStatus,
        TripStatus? TripStatus);

    public sealed record IncidentGpsEventRequest(
        string GpsEventId,
        string Event,
        string BoatCode,
        DateTimeOffset OccurredAt,
        decimal? Lat,
        decimal? Lng,
        Guid? StationId,
        string? StationCode,
        string? Note,
        string? PreviousMissionStatus,
        int? EstimatedTowingMinutes);

    public sealed record CompleteRescueMissionRequest(
        Guid IncidentId,
        string BoatCode,
        string RescueBoatCode,
        DateTimeOffset? CompletedAt,
        string? Note);
}
