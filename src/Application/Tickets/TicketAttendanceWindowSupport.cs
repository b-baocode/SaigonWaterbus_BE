using FluentValidation.Results;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Tickets;

internal static class TicketAttendanceWindowSupport
{
    public const int CheckInLeadMinutes = 10;
    public const int CheckOutGraceMinutes = 10;

    public static void EnsureCanCheckInAt(Ticket ticket, DateTimeOffset now)
    {
        EnsureCanCheckInAt(ticket, ticket.Booking, now);
    }

    public static void EnsureCanCheckInAt(Ticket ticket, Booking booking, DateTimeOffset now)
    {
        if (!TryResolveSegmentTimes(ticket, booking, out var segmentTimes))
        {
            return;
        }

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
        if (!TryResolveSegmentTimes(ticket, booking, out var segmentTimes))
        {
            return;
        }

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
        if (!now.HasValue || !TryResolveSegmentTimes(ticket, booking, out var segmentTimes))
        {
            return true;
        }

        return now.Value >= segmentTimes.Departure.AddMinutes(-CheckInLeadMinutes)
            && now.Value <= segmentTimes.Departure;
    }

    public static bool IsWithinCheckInWindow(Booking booking, BookingPassenger? passenger, DateTimeOffset? now)
    {
        if (!now.HasValue || !TryResolveSegmentTimes(booking, passenger, out var segmentTimes))
        {
            return true;
        }

        return now.Value >= segmentTimes.Departure.AddMinutes(-CheckInLeadMinutes)
            && now.Value <= segmentTimes.Departure;
    }

    public static bool IsWithinCheckOutWindow(Ticket ticket, DateTimeOffset? now)
    {
        return IsWithinCheckOutWindow(ticket, ticket.Booking, now);
    }

    public static bool IsWithinCheckOutWindow(Ticket ticket, Booking booking, DateTimeOffset? now)
    {
        if (!now.HasValue || !TryResolveSegmentTimes(ticket, booking, out var segmentTimes))
        {
            return true;
        }

        return now.Value <= segmentTimes.Arrival.AddMinutes(CheckOutGraceMinutes);
    }

    public static bool IsWithinCheckOutWindow(Booking booking, BookingPassenger? passenger, DateTimeOffset? now)
    {
        if (!now.HasValue || !TryResolveSegmentTimes(booking, passenger, out var segmentTimes))
        {
            return true;
        }

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
}
