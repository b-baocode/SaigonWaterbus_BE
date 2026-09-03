using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Trips;

public sealed record TripStopEventTargetResult(
    TripStop? Stop,
    string? Error)
{
    public bool IsSuccess => Stop is not null && string.IsNullOrWhiteSpace(Error);
}

/// <summary>
/// Resolves GPS stop events against a concrete trip-stop occurrence. A station can
/// appear more than once on a route, so station id alone is not a stable identity.
/// </summary>
public static class TripStopEventSupport
{
    public static TripStopEventTargetResult ResolveTarget(
        IEnumerable<TripStop> tripStops,
        Guid stationId,
        string eventType,
        Guid? tripStopId = null,
        int? stopOrder = null)
    {
        var orderedStops = tripStops
            .OrderBy(x => x.StopOrder)
            .ThenBy(x => x.Id)
            .ToArray();
        var stationStops = orderedStops
            .Where(x => x.StationId == stationId)
            .ToArray();

        if (stationStops.Length == 0)
        {
            return Failure("stationId không thuộc trip/route này.");
        }

        TripStop? target;
        if (tripStopId.HasValue)
        {
            target = stationStops.SingleOrDefault(x => x.Id == tripStopId.Value);
            if (target is null)
            {
                return Failure("tripStopId không khớp stationId hoặc không thuộc trip này.");
            }

            if (stopOrder.HasValue && target.StopOrder != stopOrder.Value)
            {
                return Failure("tripStopId và stopOrder không cùng xác định một điểm dừng.");
            }
        }
        else if (stopOrder.HasValue)
        {
            target = stationStops.SingleOrDefault(x => x.StopOrder == stopOrder.Value);
            if (target is null)
            {
                return Failure("stopOrder không khớp stationId hoặc không thuộc trip này.");
            }
        }
        else
        {
            target = ResolveLegacyStationTarget(orderedStops, stationStops, eventType);
        }

        if (target is null)
        {
            return Failure("Không xác định được lần cập bến tương ứng cho sự kiện GPS.");
        }

        var priorStops = orderedStops.Where(x => x.StopOrder < target.StopOrder).ToArray();
        if (priorStops.Any(x => !IsClosed(x)))
        {
            return Failure(
                $"Không thể cập nhật bến thứ {target.StopOrder} trước khi các bến trước đã rời hoặc được bỏ qua.");
        }

        if (!CanTransition(target, eventType, orderedStops[0].StopOrder))
        {
            return Failure(
                $"Không thể chuyển bến thứ {target.StopOrder} từ {target.StopStatus} sang {eventType}.");
        }

        return new TripStopEventTargetResult(target, null);
    }

    public static void ApplyEvent(
        TripStop tripStop,
        string eventType,
        DateTimeOffset occurredAt,
        string? note)
    {
        if (!string.IsNullOrWhiteSpace(note))
        {
            tripStop.Note = note.Trim();
        }

        // Repeated hook calls are idempotent and must not move an actual timestamp.
        if (string.Equals(eventType, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase))
        {
            tripStop.ActualArrivalTime ??= occurredAt;
        }
        else if (string.Equals(eventType, TripStopStatuses.Departed, StringComparison.OrdinalIgnoreCase))
        {
            tripStop.ActualArrivalTime ??= occurredAt;
            tripStop.ActualDepartureTime ??= occurredAt;
        }
        else if (string.Equals(eventType, TripStopStatuses.Skipped, StringComparison.OrdinalIgnoreCase))
        {
            tripStop.ActualDepartureTime ??= occurredAt;
        }

        // Do not regress Arrived back to Arriving on a delayed duplicate event.
        if (!string.Equals(tripStop.StopStatus, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(eventType, TripStopStatuses.Arriving, StringComparison.OrdinalIgnoreCase))
        {
            tripStop.StopStatus = eventType;
        }
    }

    private static TripStop? ResolveLegacyStationTarget(
        IReadOnlyList<TripStop> orderedStops,
        IReadOnlyList<TripStop> stationStops,
        string eventType)
    {
        var eligibleActiveStops = stationStops
            .Where(x => !IsClosed(x))
            .Where(x => orderedStops
                .Where(previous => previous.StopOrder < x.StopOrder)
                .All(IsClosed))
            .ToArray();

        var sameState = eligibleActiveStops.FirstOrDefault(x =>
            string.Equals(x.StopStatus, eventType, StringComparison.OrdinalIgnoreCase));
        if (sameState is not null)
        {
            return sameState;
        }

        if (string.Equals(eventType, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase))
        {
            var arriving = eligibleActiveStops.FirstOrDefault(x =>
                string.Equals(x.StopStatus, TripStopStatuses.Arriving, StringComparison.OrdinalIgnoreCase));
            if (arriving is not null)
            {
                return arriving;
            }
        }

        if (string.Equals(eventType, TripStopStatuses.Departed, StringComparison.OrdinalIgnoreCase))
        {
            var docked = eligibleActiveStops.FirstOrDefault(x =>
                string.Equals(x.StopStatus, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.StopStatus, TripStopStatuses.Arriving, StringComparison.OrdinalIgnoreCase));
            if (docked is not null)
            {
                return docked;
            }
        }

        var nextEligible = eligibleActiveStops.FirstOrDefault();
        if (nextEligible is not null)
        {
            return nextEligible;
        }

        // Return the first blocked active occurrence so the caller gets a sequence error.
        var blockedActive = stationStops.FirstOrDefault(x => !IsClosed(x));
        if (blockedActive is not null)
        {
            return blockedActive;
        }

        // A duplicate terminal event belongs to the most recent occurrence and is a no-op.
        return stationStops.LastOrDefault(x =>
            string.Equals(x.StopStatus, eventType, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsClosed(TripStop stop) =>
        stop.ActualDepartureTime.HasValue
        || string.Equals(stop.StopStatus, TripStopStatuses.Departed, StringComparison.OrdinalIgnoreCase)
        || string.Equals(stop.StopStatus, TripStopStatuses.Skipped, StringComparison.OrdinalIgnoreCase);

    private static bool CanTransition(TripStop stop, string eventType, int firstStopOrder)
    {
        var currentStatus = stop.StopStatus;
        if (string.Equals(currentStatus, eventType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(currentStatus, TripStopStatuses.Scheduled, StringComparison.OrdinalIgnoreCase))
        {
            return !string.Equals(eventType, TripStopStatuses.Departed, StringComparison.OrdinalIgnoreCase)
                || stop.StopOrder == firstStopOrder;
        }

        if (string.Equals(currentStatus, TripStopStatuses.Arriving, StringComparison.OrdinalIgnoreCase))
        {
            return !string.Equals(eventType, TripStopStatuses.Scheduled, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(currentStatus, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(eventType, TripStopStatuses.Departed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(eventType, TripStopStatuses.Skipped, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static TripStopEventTargetResult Failure(string message) => new(null, message);
}
