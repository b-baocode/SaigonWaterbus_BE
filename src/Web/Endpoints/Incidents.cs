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

    private const string AssignManagerExample =
        """
        {
          "managerUserId": "00000000-0000-0000-0000-000000000000"
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

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetIncidentList, string.Empty)
            .RequireAuthorization()
            .WithSummary("Danh sach su co tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Query params optional: boatId, tripId, resolutionStatus.",
                "Dung de xem cac su co dang Open hoac da Resolved."));

        group.MapPost(CreateIncident, string.Empty)
            .RequireAuthorization()
            .WithSummary("Bao su co tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                CreateIncidentExample,
                "boatId bat buoc. tripId optional neu su co xay ra trong mot chuyen cu the.",
                "Khi tao su co, tau duoc chuyen sang Incident.",
                "Neu co tripId: severity High/Critical se Cancelled trip; muc khac se Delayed trip."));

        group.MapPatch(AssignManager, "{incidentId:guid}/assign-manager")
            .RequireAuthorization()
            .WithSummary("Admin gan manager phu trach su co")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                AssignManagerExample,
                "managerUserId phai la user role Manager dang Active.",
                "Dung sau khi staff bao su co de giao nguoi phu trach xu ly."));

        group.MapPatch(AssignReplacementBoat, "{incidentId:guid}/assign-replacement-boat")
            .RequireAuthorization()
            .WithSummary("Gan tau thay the/cuu ho cho su co")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager",
                AssignReplacementBoatExample,
                "rescueBoatId bat buoc, phai la tau serviceType Rescue dang Active.",
                "BE tinh khach bi anh huong theo vi tri tau: onboardPassengerCount va futurePassengerCount.",
                "Neu co khach dang tren tau: replacementBoatId bat buoc, mission TransferAtIncidentLocation.",
                "Neu chua co khach tren tau nhung co khach cho o ben sau: replacementBoatId bat buoc, mission ContinueFromStation va co replacementTargetStation.",
                "Neu khong co khach bi anh huong: chi gui rescueBoatId; replacementBoatId phai de null.",
                "Tau thay the phai serviceType Passenger, Active, setup du ghe va khong trung tau cuu ho/tau gap su co.",
                "delayMinutes la so phut tre du kien; BE luu replacementEstimatedResumeAt, trip adjustedStart/End va adjusted time cho cac stop con lai."));

        group.MapPatch(ResolveIncident, "{incidentId:guid}/resolve")
            .RequireAuthorization()
            .WithSummary("Xu ly/dong su co tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager",
                ResolveIncidentExample,
                "resolutionNote bat buoc.",
                "boatStatus/tripStatus optional de cap nhat trang thai sau khi xu ly."));

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

    private static async Task<IResult> AssignManager(
        ISender sender,
        Guid incidentId,
        AssignManagerRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new AssignIncidentManagerCommand(incidentId, request.ManagerUserId),
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

    public sealed record AssignManagerRequest(Guid ManagerUserId);

    public sealed record AssignReplacementBoatRequest(
        Guid RescueBoatId,
        Guid? ReplacementBoatId,
        int? DelayMinutes,
        string? Note);

    public sealed record ResolveIncidentRequest(
        string ResolutionNote,
        BoatStatus? BoatStatus,
        TripStatus? TripStatus);

    public sealed record CompleteRescueMissionRequest(
        Guid IncidentId,
        string BoatCode,
        string RescueBoatCode,
        DateTimeOffset? CompletedAt,
        string? Note);
}
