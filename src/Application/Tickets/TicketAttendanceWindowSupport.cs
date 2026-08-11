using FluentValidation.Results;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Tickets;

public static class TicketAttendanceWindowSupport
{
    public const int CheckInLeadMinutes = 10;
    public const int CheckOutGraceMinutes = 10;

    public static void EnsureCanCheckInAt(Ticket ticket, DateTimeOffset now)
    {
        EnsureCanCheckInAt(ticket, ticket.Booking, now);
    }

    public static void EnsureCanCheckInAt(Ticket ticket, Booking booking, DateTimeOffset now)
    {
        var passenger = ResolveTicketPassenger(ticket, booking);
        var trip = ResolveTicketTrip(booking, passenger);
        if (trip is null)
        {
            return;
        }

        if (trip.TripStops.Count > 0)
        {
            var boardingStop = ResolveBoardingStop(trip, passenger);
            if (boardingStop is null)
            {
                throw new ValidationException([new ValidationFailure("ticket",
                    "Không xác định được bến khách lên của vé, chưa thể check-in.")]);
            }

            EnsureStopOpenForScan(
                boardingStop,
                now,
                "Tàu chưa cập bến khách lên, chưa thể check-in.",
                "Tàu đã rời bến khách lên, không thể check-in.",
                "Đã quá thời gian dừng tại bến khách lên, không thể check-in.");
            return;
        }

        var segmentTimes = TripStopScheduleSupport.ResolveSegmentTimes(
            trip,
            passenger?.FromStopOrder,
            passenger?.ToStopOrder);
        var earliestCheckIn = segmentTimes.Departure.AddMinutes(-CheckInLeadMinutes);
        if (now < earliestCheckIn)
        {
            throw new ValidationException([new ValidationFailure("ticket",
                "Chỉ được check-in trong vòng 10 phút trước giờ tàu rời bến khách lên.")]);
        }

        if (now > segmentTimes.Departure)
        {
            throw new ValidationException([new ValidationFailure("ticket",
                "Đã quá giờ tàu rời bến khách lên, không thể check-in.")]);
        }
    }

    public static void EnsureCanCheckOutAt(Ticket ticket, DateTimeOffset now)
    {
        EnsureCanCheckOutAt(ticket, ticket.Booking, now);
    }

    public static void EnsureCanCheckOutAt(Ticket ticket, Booking booking, DateTimeOffset now)
    {
        var passenger = ResolveTicketPassenger(ticket, booking);
        var trip = ResolveTicketTrip(booking, passenger);
        if (trip is null)
        {
            return;
        }

        if (trip.TripStops.Count > 0)
        {
            var alightingStop = ResolveAlightingStop(trip, passenger);
            if (alightingStop is null)
            {
                throw new ValidationException([new ValidationFailure("ticket",
                    "Không xác định được bến khách xuống của vé, chưa thể check-out.")]);
            }

            EnsureStopOpenForScan(
                alightingStop,
                now,
                "Tàu chưa cập bến khách xuống, chưa thể check-out.",
                "Tàu đã rời bến khách xuống, không thể check-out.",
                "Đã quá thời gian dừng tại bến khách xuống, không thể check-out.");
            return;
        }

        var segmentTimes = TripStopScheduleSupport.ResolveSegmentTimes(
            trip,
            passenger?.FromStopOrder,
            passenger?.ToStopOrder);
        var latestCheckOut = segmentTimes.Arrival.AddMinutes(CheckOutGraceMinutes);
        if (now > latestCheckOut)
        {
            throw new ValidationException([new ValidationFailure("ticket",
                "Đã quá 10 phút sau giờ tàu đến bến khách xuống, không thể check-out.")]);
        }
    }

    public static bool IsWithinCheckInWindow(Ticket ticket, DateTimeOffset? now)
    {
        return IsWithinCheckInWindow(ticket, ticket.Booking, now);
    }

    public static bool IsWithinCheckInWindow(Ticket ticket, Booking booking, DateTimeOffset? now)
    {
        var passenger = ResolveTicketPassenger(ticket, booking);
        var trip = ResolveTicketTrip(booking, passenger);
        return IsWithinCheckInWindow(trip, passenger, now);
    }

    public static bool IsWithinCheckInWindow(Booking booking, BookingPassenger? passenger, DateTimeOffset? now)
    {
        var trip = ResolveTicketTrip(booking, passenger);
        return IsWithinCheckInWindow(trip, passenger, now);
    }

    public static bool IsWithinCheckInWindow(Trip? trip, BookingPassenger? passenger, DateTimeOffset? now)
    {
        if (!now.HasValue || trip is null)
        {
            return true;
        }

        if (trip.TripStops.Count > 0)
        {
            var boardingStop = ResolveBoardingStop(trip, passenger);
            return boardingStop is not null && IsStopOpenForScan(boardingStop, now.Value);
        }

        var segmentTimes = TripStopScheduleSupport.ResolveSegmentTimes(
            trip,
            passenger?.FromStopOrder,
            passenger?.ToStopOrder);
        return now.Value >= segmentTimes.Departure.AddMinutes(-CheckInLeadMinutes)
            && now.Value <= segmentTimes.Departure;
    }

    public static bool IsWithinCheckOutWindow(Ticket ticket, DateTimeOffset? now)
    {
        return IsWithinCheckOutWindow(ticket, ticket.Booking, now);
    }

    public static bool IsWithinCheckOutWindow(Ticket ticket, Booking booking, DateTimeOffset? now)
    {
        if (!now.HasValue)
        {
            return true;
        }

        var passenger = ResolveTicketPassenger(ticket, booking);
        var trip = ResolveTicketTrip(booking, passenger);
        if (trip is null)
        {
            return true;
        }

        if (trip.TripStops.Count > 0)
        {
            var alightingStop = ResolveAlightingStop(trip, passenger);
            return alightingStop is not null && IsStopOpenForScan(alightingStop, now.Value);
        }

        var segmentTimes = TripStopScheduleSupport.ResolveSegmentTimes(
            trip,
            passenger?.FromStopOrder,
            passenger?.ToStopOrder);
        return now.Value <= segmentTimes.Arrival.AddMinutes(CheckOutGraceMinutes);
    }

    public static bool IsWithinCheckOutWindow(Booking booking, BookingPassenger? passenger, DateTimeOffset? now)
    {
        if (!now.HasValue)
        {
            return true;
        }

        var trip = ResolveTicketTrip(booking, passenger);
        if (trip is null)
        {
            return true;
        }

        if (trip.TripStops.Count > 0)
        {
            var alightingStop = ResolveAlightingStop(trip, passenger);
            return alightingStop is not null && IsStopOpenForScan(alightingStop, now.Value);
        }

        var segmentTimes = TripStopScheduleSupport.ResolveSegmentTimes(
            trip,
            passenger?.FromStopOrder,
            passenger?.ToStopOrder);
        return now.Value <= segmentTimes.Arrival.AddMinutes(CheckOutGraceMinutes);
    }

    private static bool TryResolveSegmentTimes(
        Ticket ticket,
        Booking booking,
        out (DateTimeOffset Departure, DateTimeOffset Arrival) segmentTimes)
    {
        var passenger = ResolveTicketPassenger(ticket, booking);
        return TryResolveSegmentTimes(booking, passenger, out segmentTimes);
    }

    private static bool TryResolveSegmentTimes(
        Booking booking,
        BookingPassenger? passenger,
        out (DateTimeOffset Departure, DateTimeOffset Arrival) segmentTimes)
    {
        var trip = ResolveTicketTrip(booking, passenger);
        if (trip is null)
        {
            segmentTimes = default;
            return false;
        }

        segmentTimes = TripStopScheduleSupport.ResolveSegmentTimes(
            trip,
            passenger?.FromStopOrder,
            passenger?.ToStopOrder);
        return true;
    }

    private static BookingPassenger? ResolveTicketPassenger(Ticket ticket, Booking booking)
    {
        if (ticket.BookingPassenger is not null)
        {
            return ticket.BookingPassenger;
        }

        return ticket.BookingPassengerId.HasValue
            ? booking.Passengers.FirstOrDefault(x => x.Id == ticket.BookingPassengerId.Value)
            : null;
    }

    private static Trip? ResolveTicketTrip(Booking booking, BookingPassenger? passenger)
    {
        if (passenger?.Trip is not null)
        {
            return passenger.Trip;
        }

        if (passenger?.TripId == booking.ReturnTripId)
        {
            return booking.ReturnTrip;
        }

        if (passenger?.TripId == booking.TripId)
        {
            return booking.Trip;
        }

        return booking.Trip;
    }

    private static TripStop? ResolveBoardingStop(Trip trip, BookingPassenger? passenger)
    {
        var orderedStops = trip.TripStops
            .OrderBy(x => x.StopOrder)
            .ToArray();

        if (passenger?.FromStopOrder is int fromStopOrder)
        {
            return orderedStops.FirstOrDefault(x => x.StopOrder == fromStopOrder);
        }

        if (passenger?.FromStationId is Guid fromStationId)
        {
            return orderedStops.FirstOrDefault(x => x.StationId == fromStationId);
        }

        return orderedStops.FirstOrDefault();
    }

    private static TripStop? ResolveAlightingStop(Trip trip, BookingPassenger? passenger)
    {
        var orderedStops = trip.TripStops
            .OrderBy(x => x.StopOrder)
            .ToArray();

        if (passenger?.ToStopOrder is int toStopOrder)
        {
            return orderedStops.FirstOrDefault(x => x.StopOrder == toStopOrder);
        }

        if (passenger?.ToStationId is Guid toStationId)
        {
            return orderedStops.FirstOrDefault(x => x.StationId == toStationId);
        }

        return orderedStops.LastOrDefault();
    }

    private static void EnsureStopOpenForScan(
        TripStop stop,
        DateTimeOffset now,
        string notArrivedMessage,
        string departedMessage,
        string dwellExpiredMessage)
    {
        if (stop.ActualDepartureTime.HasValue
            || string.Equals(stop.StopStatus, TripStopStatuses.Departed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(stop.StopStatus, TripStopStatuses.Skipped, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure("ticket", departedMessage)]);
        }

        if (!string.Equals(stop.StopStatus, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase)
            || !stop.ActualArrivalTime.HasValue)
        {
            throw new ValidationException([new ValidationFailure("ticket", notArrivedMessage)]);
        }

        var stopScanEndsAt = ResolveStopScanEndsAt(stop);
        if (stopScanEndsAt.HasValue && now > stopScanEndsAt.Value)
        {
            throw new ValidationException([new ValidationFailure("ticket", dwellExpiredMessage)]);
        }
    }

    private static bool IsStopOpenForScan(TripStop stop, DateTimeOffset now)
    {
        if (!string.Equals(stop.StopStatus, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase)
            || !stop.ActualArrivalTime.HasValue
            || stop.ActualDepartureTime.HasValue)
        {
            return false;
        }

        var stopScanEndsAt = ResolveStopScanEndsAt(stop);
        return !stopScanEndsAt.HasValue || now <= stopScanEndsAt.Value;
    }

    private static DateTimeOffset? ResolveStopScanEndsAt(TripStop stop)
    {
        if (stop.StayDurationMinutes > 0 && stop.ActualArrivalTime.HasValue)
        {
            return stop.ActualArrivalTime.Value.AddMinutes(stop.StayDurationMinutes);
        }

        return null;
    }
}
