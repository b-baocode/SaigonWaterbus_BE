using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Incidents;

public static class IncidentGpsBoatRoles
{
    public const string Incident = "Incident";
    public const string Rescue = "Rescue";
    public const string Replacement = "Replacement";
}

internal static class IncidentGpsMissionSupport
{
    public static string ResolveExpectedBoatRole(string eventType) => eventType switch
    {
        IncidentGpsEventTypes.RescueArrived => IncidentGpsBoatRoles.Rescue,
        IncidentGpsEventTypes.ReplacementArrived => IncidentGpsBoatRoles.Replacement,
        IncidentGpsEventTypes.PassengerTransferCompleted => IncidentGpsBoatRoles.Replacement,
        IncidentGpsEventTypes.TowingStarted => IncidentGpsBoatRoles.Rescue,
        IncidentGpsEventTypes.TowingCompleted => IncidentGpsBoatRoles.Rescue,
        _ => IncidentGpsBoatRoles.Incident
    };

    public static string? ResolveBoatRole(Incident incident, string? boatCode)
    {
        if (string.IsNullOrWhiteSpace(boatCode))
        {
            return null;
        }

        var normalizedBoatCode = boatCode.Trim();
        if (string.Equals(incident.Boat.Code, normalizedBoatCode, StringComparison.OrdinalIgnoreCase))
        {
            return IncidentGpsBoatRoles.Incident;
        }

        if (incident.RescueBoat is not null
            && string.Equals(incident.RescueBoat.Code, normalizedBoatCode, StringComparison.OrdinalIgnoreCase))
        {
            return IncidentGpsBoatRoles.Rescue;
        }

        return incident.ReplacementBoat is not null
            && string.Equals(incident.ReplacementBoat.Code, normalizedBoatCode, StringComparison.OrdinalIgnoreCase)
                ? IncidentGpsBoatRoles.Replacement
                : null;
    }

    public static DateTimeOffset? ResolveAuthoritativeResumeAt(Incident incident)
    {
        var resumeAt = incident.ReplacementEstimatedResumeAt;
        if (incident.ReplacementMissionType != IncidentReplacementMissionTypes.ContinueFromStation
            || incident.Trip is null
            || !incident.ReplacementTargetStopOrder.HasValue)
        {
            return resumeAt;
        }

        var targetStop = incident.Trip.TripStops.FirstOrDefault(
            x => x.StopOrder == incident.ReplacementTargetStopOrder.Value);
        var adjustedTargetDeparture = targetStop?.AdjustedDepartureTime
            ?? targetStop?.AdjustedArrivalTime;
        if (!adjustedTargetDeparture.HasValue)
        {
            return resumeAt;
        }

        return !resumeAt.HasValue || adjustedTargetDeparture.Value > resumeAt.Value
            ? adjustedTargetDeparture
            : resumeAt;
    }

    public static bool CanReplacementContinueTrip(Incident incident, DateTimeOffset now)
    {
        var resumeAt = ResolveAuthoritativeResumeAt(incident);
        return incident.ReplacementBoatId.HasValue
            && incident.ReplacementArrivedAt.HasValue
            && (incident.OnboardPassengerCountSnapshot <= 0
                || incident.PassengerTransferCompletedAt.HasValue)
            && incident.Trip?.BoatId == incident.ReplacementBoatId
            && incident.Trip.TripStatus is not TripStatus.Completed and not TripStatus.Cancelled
            && (!resumeAt.HasValue || now >= resumeAt.Value);
    }

    public static bool CanRescueStartTowing(Incident incident) =>
        incident.RescueArrivedAt.HasValue
        && (incident.OnboardPassengerCountSnapshot <= 0 || incident.PassengerTransferCompletedAt.HasValue)
        && !incident.TowingStartedAt.HasValue;

    public static IReadOnlyList<string> ResolveRescueNextEvents(Incident incident)
    {
        if (!incident.RescueBoatId.HasValue || incident.TowingCompletedAt.HasValue)
        {
            return [];
        }

        if (!incident.RescueArrivedAt.HasValue)
        {
            return [IncidentGpsEventTypes.RescueArrived];
        }

        return !incident.TowingStartedAt.HasValue
            ? CanRescueStartTowing(incident) ? [IncidentGpsEventTypes.TowingStarted] : []
            : [IncidentGpsEventTypes.TowingCompleted];
    }

    public static IReadOnlyList<string> ResolveReplacementNextEvents(Incident incident)
    {
        if (!incident.ReplacementBoatId.HasValue)
        {
            return [];
        }

        if (!incident.ReplacementArrivedAt.HasValue)
        {
            return [IncidentGpsEventTypes.ReplacementArrived];
        }

        return incident.OnboardPassengerCountSnapshot > 0
                && !incident.PassengerTransferCompletedAt.HasValue
            ? [IncidentGpsEventTypes.PassengerTransferCompleted]
            : [];
    }
}
