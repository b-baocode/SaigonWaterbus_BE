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
    public void OneWayTicketIsPastCheckInTwoMinutesAfterBoatLeavesBoardingStop()
    {
        var (ticket, booking) = BuildOneWay();
        var boarding = FirstStop(booking.Trip!);
        MarkArrived(boarding, Departure.AddMinutes(-8));
        MarkDeparted(boarding, Departure);

        TicketAttendanceWindowSupport
            .IsPastCheckInWindow(ticket, booking, Departure.AddMinutes(2))
            .ShouldBeFalse();
        TicketAttendanceWindowSupport
            .IsPastCheckInWindow(ticket, booking, Departure.AddMinutes(2).AddSeconds(1))
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

    /// <summary>
    /// Ca thật ngoài production: bến cuối LUÔN có StayDurationMinutes = 0 và tàu không bao giờ
    /// "rời" bến cuối, nên trước khi có mốc dừng giả định thì vé của khách quên check-out treo
    /// CheckedIn vĩnh viễn — booking không bao giờ Completed, khách mất điểm.
    /// </summary>
    [Test]
    public void CheckedInTicketAtTerminalStopExpiresEvenWhenBoatNeverDeparts()
    {
        var (ticket, booking) = BuildOneWay();
        UseProductionDwell(booking.Trip!);
        CheckIn(ticket);
        // Nhân viên bấm cập bến cuối rồi thôi — không có sự kiện rời bến để bấm.
        MarkArrived(LastStop(booking.Trip!), Departure.AddMinutes(50));

        TicketAttendanceWindowSupport
            .IsPastCheckOutWindow(ticket, booking, Departure.AddMinutes(64))
            .ShouldBeFalse("còn trong 10 phút dừng giả định + 5 phút ân hạn");
        TicketAttendanceWindowSupport
            .IsPastCheckOutWindow(ticket, booking, Departure.AddMinutes(66))
            .ShouldBeTrue();
    }

    /// <summary>
    /// Sightseeing không lưu stop order nên bến xuống luôn rơi về bến cuối — tức MỌI vé
    /// sightseeing đều đi qua đúng nhánh vừa vá.
    /// </summary>
    [Test]
    public void SightseeingTicketExpiresAfterTheLoopEnds()
    {
        var (ticket, booking) = BuildOneWay(useSegments: false);
        UseProductionDwell(booking.Trip!);
        CheckIn(ticket);
        MarkArrived(LastStop(booking.Trip!), Departure.AddMinutes(50));

        TicketAttendanceWindowSupport
            .IsPastCheckOutWindow(ticket, booking, Departure.AddMinutes(66))
            .ShouldBeTrue();
    }

    /// <summary>Nhân viên không bấm cập bến thì rơi về giờ dự kiến, vẫn phải có hạn.</summary>
    [Test]
    public void CheckedInTicketExpiresEvenWhenNobodyMarksTheStopArrived()
    {
        var (ticket, booking) = BuildOneWay();
        UseProductionDwell(booking.Trip!);
        CheckIn(ticket);

        TicketAttendanceWindowSupport
            .IsPastCheckOutWindow(ticket, booking, Departure.AddMinutes(64))
            .ShouldBeFalse();
        TicketAttendanceWindowSupport
            .IsPastCheckOutWindow(ticket, booking, Departure.AddMinutes(66))
            .ShouldBeTrue();
    }

    /// <summary>
    /// Chuyến trễ có ghi nhận: hạn phải giãn theo adjusted_arrival_time, nếu không thì vé của
    /// khách CÒN ĐANG NGỒI TRÊN TÀU bị huỷ và quầy không check-out được nữa.
    /// </summary>
    [Test]
    public void RecordedDelayPushesTheCheckOutDeadline()
    {
        var (ticket, booking) = BuildOneWay();
        UseProductionDwell(booking.Trip!);
        CheckIn(ticket);
        LastStop(booking.Trip!).AdjustedArrivalTime = Departure.AddMinutes(90);

        TicketAttendanceWindowSupport
            .IsPastCheckOutWindow(ticket, booking, Departure.AddMinutes(66))
            .ShouldBeFalse("chuyến trễ 40 phút, khách còn trên tàu");
        TicketAttendanceWindowSupport
            .IsPastCheckOutWindow(ticket, booking, Departure.AddMinutes(106))
            .ShouldBeTrue();
    }

    /// <summary>
    /// Hàng rào cho đường check-IN: bến ĐẦU cũng có StayDurationMinutes = 0. Mốc dừng giả định
    /// tuyệt đối không được lan sang đây — tàu đỗ bến đầu lâu (khởi hành trễ) mà vé Active bị
    /// giết thì khách chưa kịp lên tàu đã mất vé.
    /// </summary>
    [Test]
    public void ActiveTicketSurvivesLongDwellAtBoardingStop()
    {
        var (ticket, booking) = BuildOneWay();
        UseProductionDwell(booking.Trip!);
        MarkArrived(FirstStop(booking.Trip!), Departure.AddMinutes(-10));

        TicketAttendanceWindowSupport
            .IsPastCheckInWindow(ticket, booking, Departure.AddMinutes(60))
            .ShouldBeFalse("tàu vẫn đang đỗ bến đầu, chưa rời bến");
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

    /// <summary>
    /// Đưa thời gian dừng về đúng dữ liệu thật: bến đầu và bến cuối luôn 0 phút
    /// (<c>TripStopScheduleSupport.ResolveStayDurationMinutes</c>), chỉ bến giữa mới có thời gian dừng.
    /// Helper dựng trip mặc định cho mọi bến 5 phút nên vốn không chạm được vào ca lỗi này.
    /// </summary>
    private static void UseProductionDwell(Trip trip)
    {
        var stops = trip.TripStops.OrderBy(x => x.StopOrder).ToArray();
        stops.First().StayDurationMinutes = 0;
        stops.Last().StayDurationMinutes = 0;
    }

    private static void CheckIn(Ticket ticket)
    {
        ticket.TicketStatus = TicketStatus.CheckedIn;
        ticket.CheckedInAt = Departure.AddMinutes(-5);
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
