using Microsoft.AspNetCore.Mvc;
using SaigonWaterbus.Application.Incidents;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Incidents : IEndpointGroup
{
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
                "Neu trip co ve/khach dang hieu luc, replacementBoatId bat buoc va backend cap nhat trip.BoatId sang tau thay the.",
                "Neu khong co khach can chuyen, chi gui rescueBoatId; replacementBoatId phai de null.",
                "Tau thay the phai serviceType Passenger, Active, setup du ghe va khong trung tau cuu ho/tau gap su co.",
                "delayMinutes chi anh huong trang thai trip khi incident co tripId."));

        group.MapPatch(ResolveIncident, "{incidentId:guid}/resolve")
            .RequireAuthorization()
            .WithSummary("Xu ly/dong su co tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager",
                ResolveIncidentExample,
                "resolutionNote bat buoc.",
                "boatStatus/tripStatus optional de cap nhat trang thai sau khi xu ly."));
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
}
