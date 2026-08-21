using NUnit.Framework;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Tickets;

/// <summary>
/// Chốt hành vi của hai hàm mà job dọn vé nền dùng để quyết định huỷ vé.
///
/// Trước đây job hỏi "vé có ĐANG trong cửa sổ quét không" rồi hiểu câu trả lời "không" thành
/// "đã hết hạn" — nhưng vé mua trước ngày đi cũng trả "không". Hậu quả thật trên production:
/// booking khứ hồi BK-20260821-DSL4L bị huỷ cả hai vé một phút sau khi thanh toán, hai tiếng
/// trước giờ tàu chạy.
/// </summary>
public class TicketExpirationWindowTests
{
    private static readonly DateTimeOffset Departure =
        new(2026, 5, 1, 9, 0, 0, TimeSpan.FromHours(7));

    [Test]
    public void OneWayTicketBoughtDaysAheadIsNotPastCheckIn()
    {
        var (ticket, booking) = BuildOneWay();

        TicketAttendanceWindowSupport
            .IsPastCheckInWindow(ticket, booking, Departure.AddDays(-3))
            .ShouldBeFalse();
    }

    [Test]
    public void OneWayTicketIsPastCheckInOnceBoatLeavesBoardingStop()
    {
        var (ticket, booking) = BuildOneWay();
        var boarding = FirstStop(booking.Trip!);
        MarkArrived(boarding, Departure.AddMinutes(-8));
        MarkDeparted(boarding, Departure);

        TicketAttendanceWindowSupport
            .IsPastCheckInWindow(ticket, booking, Departure.AddMinutes(1))
            .ShouldBeTrue();
    }

    [Test]
    public void CheckedInTicketSurvivesTheWholeVoyage()
    {
        var (ticket, booking) = BuildOneWay();
        ticket.TicketStatus = TicketStatus.CheckedIn;
        ticket.CheckedInAt = Departure.AddMinutes(-5);
        var boarding = FirstStop(booking.Trip!);
        MarkArrived(boarding, Departure.AddMinutes(-8));
        MarkDeparted(boarding, Departure);

        TicketAttendanceWindowSupport
            .IsPastCheckOutWindow(ticket, booking, Departure.AddMinutes(20))
            .ShouldBeFalse();
    }

    [Test]
    public void CheckedInTicketIsPastCheckOutOnceBoatLeavesAlightingStop()
    {
        var (ticket, booking) = BuildOneWay();
        ticket.TicketStatus = TicketStatus.CheckedIn;
        ticket.CheckedInAt = Departure.AddMinutes(-5);
        var alighting = LastStop(booking.Trip!);
        MarkArrived(alighting, Departure.AddMinutes(50));
        MarkDeparted(alighting, Departure.AddMinutes(55));

        TicketAttendanceWindowSupport
            .IsPastCheckOutWindow(ticket, booking, Departure.AddMinutes(56))
            .ShouldBeTrue();
    }

    /// <summary>Ca tái hiện đúng bug đã thấy trên production.</summary>
    [Test]
    public void RoundTripTicketsAreNotPastCheckInBeforeTravelDay()
    {
        var (outboundTicket, returnTicket, booking) = BuildRoundTrip();
        var justAfterPayment = Departure.AddHours(-2);

        TicketAttendanceWindowSupport
            .IsPastCheckInWindow(outboundTicket, booking, justAfterPayment)
            .ShouldBeFalse();
        TicketAttendanceWindowSupport
            .IsPastCheckInWindow(returnTicket, booking, justAfterPayment)
            .ShouldBeFalse();
    }

    /// <summary>
    /// Chiều đi chạy xong KHÔNG được kéo theo vé chiều về: mỗi vé phải tự soi chặng của mình
    /// qua booking_passengers.trip_id.
    /// </summary>
    [Test]
    public void ReturnLegTicketIsNotJudgedByOutboundTrip()
    {
        var (outboundTicket, returnTicket, booking) = BuildRoundTrip();
        var outboundBoarding = FirstStop(booking.Trip!);
        MarkArrived(outboundBoarding, Departure.AddMinutes(-8));
        MarkDeparted(outboundBoarding, Departure);
        var afterOutboundLeft = Departure.AddMinutes(30);

        TicketAttendanceWindowSupport
            .IsPastCheckInWindow(outboundTicket, booking, afterOutboundLeft)
            .ShouldBeTrue("chiều đi đã rời bến nên vé chiều đi hết hạn");
        TicketAttendanceWindowSupport
            .IsPastCheckInWindow(returnTicket, booking, afterOutboundLeft)
            .ShouldBeFalse("chuyến chiều về còn chưa cập bến");
    }

    /// <summary>Sightseeing: khách chiếm ghế cả vòng, không có chặng lên/xuống riêng.</summary>
    [Test]
    public void SightseeingTicketSurvivesMidLoop()
    {
        var (ticket, booking) = BuildOneWay(useSegments: false);
        ticket.TicketStatus = TicketStatus.CheckedIn;
        ticket.CheckedInAt = Departure.AddMinutes(-5);
        var boarding = FirstStop(booking.Trip!);
        MarkArrived(boarding, Departure.AddMinutes(-8));
        MarkDeparted(boarding, Departure);

        TicketAttendanceWindowSupport
            .IsPastCheckOutWindow(ticket, booking, Departure.AddMinutes(25))
            .ShouldBeFalse();
    }

    private static (Ticket Ticket, Booking Booking) BuildOneWay(bool useSegments = true)
    {
        var trip = BuildTrip(Departure);
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            Trip = trip,
            BookingStatus = BookingStatus.Confirmed
        };
        var ticket = AddPassengerTicket(booking, trip, useSegments);
        return (ticket, booking);
    }

    private static (Ticket Outbound, Ticket Return, Booking Booking) BuildRoundTrip()
    {
        var outbound = BuildTrip(Departure);
        var inbound = BuildTrip(Departure.AddHours(6));
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            TripId = outbound.Id,
            Trip = outbound,
            ReturnTripId = inbound.Id,
            ReturnTrip = inbound,
            BookingStatus = BookingStatus.Confirmed
        };

        return (AddPassengerTicket(booking, outbound), AddPassengerTicket(booking, inbound), booking);
    }

    private static Trip BuildTrip(DateTimeOffset departure)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            DepartureTime = departure,
            ArrivalTime = departure.AddMinutes(50),
            TripStatus = TripStatus.Scheduled
        };

        for (var order = 1; order <= 3; order++)
        {
            trip.TripStops.Add(new TripStop
            {
                Id = Guid.NewGuid(),
                TripId = trip.Id,
                StationId = Guid.NewGuid(),
                StopOrder = order,
                StayDurationMinutes = 5,
                PlannedArrivalTime = departure.AddMinutes(25 * (order - 1)),
                PlannedDepartureTime = departure.AddMinutes(25 * (order - 1)),
                StopStatus = TripStopStatuses.Scheduled
            });
        }

        return trip;
    }

    private static Ticket AddPassengerTicket(Booking booking, Trip trip, bool useSegments = true)
    {
        var stops = trip.TripStops.OrderBy(x => x.StopOrder).ToArray();
        var passenger = new BookingPassenger
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            FullName = "Ngo Gia Bao",
            TripId = trip.Id,
            Trip = trip,
            FromStationId = useSegments ? stops.First().StationId : null,
            ToStationId = useSegments ? stops.Last().StationId : null,
            FromStopOrder = useSegments ? stops.First().StopOrder : null,
            ToStopOrder = useSegments ? stops.Last().StopOrder : null
        };
        booking.Passengers.Add(passenger);

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            Booking = booking,
            BookingPassengerId = passenger.Id,
            BookingPassenger = passenger,
            TicketCode = $"TK{Guid.NewGuid():N}"[..16],
            QrToken = Guid.NewGuid().ToString("N"),
            TicketStatus = TicketStatus.Active,
            IssuedAt = Departure.AddDays(-3)
        };
        booking.Tickets.Add(ticket);
        return ticket;
    }

    private static TripStop FirstStop(Trip trip) => trip.TripStops.OrderBy(x => x.StopOrder).First();

    private static TripStop LastStop(Trip trip) => trip.TripStops.OrderBy(x => x.StopOrder).Last();

    private static void MarkArrived(TripStop stop, DateTimeOffset at)
    {
        stop.StopStatus = TripStopStatuses.Arrived;
        stop.ActualArrivalTime = at;
    }

    private static void MarkDeparted(TripStop stop, DateTimeOffset at)
    {
        stop.StopStatus = TripStopStatuses.Departed;
        stop.ActualDepartureTime = at;
    }
}
