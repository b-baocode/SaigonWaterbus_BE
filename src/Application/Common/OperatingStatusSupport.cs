using SaigonWaterbus.Application.Incidents;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Common;

public static class OperatingStatusSupport
{
    public const string Idle = "Idle";
    public const string Scheduled = "Scheduled";
    public const string Boarding = "Boarding";
    public const string InProgress = "InProgress";
    public const string Delayed = "Delayed";
    public const string Incident = "Incident";
    public const string Dispatched = "Dispatched";
    public const string Maintenance = "Maintenance";
    public const string Unavailable = "Unavailable";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static string? ToPublicMissionStatus(string? missionStatus) =>
        missionStatus switch
        {
            null => null,
            IncidentMissionStatuses.IncidentCreated => null,
            IncidentMissionStatuses.RescueDispatched => IncidentMissionStatuses.Dispatched,
            IncidentMissionStatuses.ReplacementDispatched => IncidentMissionStatuses.Dispatched,
            IncidentMissionStatuses.Resolved => IncidentMissionStatuses.Completed,
            _ => missionStatus
        };

    public static string? ToPublicMissionStatus(Incident incident) =>
        string.Equals(incident.ResolutionStatus, IncidentSupport.ResolvedStatus, StringComparison.OrdinalIgnoreCase)
            ? IncidentMissionStatuses.Completed
            : ToPublicMissionStatus(incident.MissionStatus);

    public static string ForIncident(Incident incident)
    {
        if (string.Equals(incident.ResolutionStatus, IncidentSupport.ResolvedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return Completed;
        }

        return ToPublicMissionStatus(incident.MissionStatus) is null
            ? Incident
            : Dispatched;
    }

    public static string ForBoat(Boat boat, Trip? activeTrip = null, Incident? activeIncident = null)
    {
        if (boat.Status == BoatStatus.UnderMaintenance)
        {
            return Maintenance;
        }

        if (boat.Status is BoatStatus.Inactive or BoatStatus.Retired)
        {
            return Unavailable;
        }

        if (activeIncident is not null
            && !string.Equals(activeIncident.ResolutionStatus, IncidentSupport.ResolvedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return ForIncident(activeIncident);
        }

        if (boat.Status == BoatStatus.Incident)
        {
            return Incident;
        }

        return activeTrip is null ? Idle : FromTripStatus(activeTrip.TripStatus);
    }

    public static string ForTrip(Trip trip, Incident? activeIncident = null)
    {
        if (activeIncident is not null
            && !string.Equals(activeIncident.ResolutionStatus, IncidentSupport.ResolvedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return ForIncident(activeIncident);
        }

        return FromTripStatus(trip.TripStatus);
    }

    private static string FromTripStatus(TripStatus tripStatus) =>
        tripStatus switch
        {
            TripStatus.Scheduled => Scheduled,
            TripStatus.Boarding => Boarding,
            TripStatus.InProgress => InProgress,
            TripStatus.Delayed => Delayed,
            TripStatus.Completed => Completed,
            TripStatus.Cancelled => Cancelled,
            _ => InProgress
        };
}
