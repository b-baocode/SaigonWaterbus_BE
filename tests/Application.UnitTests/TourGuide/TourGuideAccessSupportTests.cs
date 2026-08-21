using NUnit.Framework;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.TourGuide;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.TourGuide;

/// <summary>
/// Chốt luật: hướng dẫn viên AI chỉ mở cho khách đang thật sự ngồi trên tàu — có vé của chính
/// tài khoản mình trên đúng chuyến đó, và vé đang CheckedIn.
///
/// Cưỡng chế bám vào TRẠNG THÁI VÉ chứ không tự tính hạn giờ; hạn giờ là việc của job dọn vé
/// (xem TicketExpirationWindowTests).
/// </summary>
public class TourGuideAccessSupportTests
{
    private static readonly DateTimeOffset Departure =
        new(2030, 5, 1, 9, 0, 0, TimeSpan.FromHours(7));

    [Test]
    public async Task CheckedInPassengerMayAskTheGuide()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customer = await SeatFlowTestData.SeedCustomerAsync(context);
        var trip = await SeedTripWithTicketAsync(context, customer.UserId!.Value, TicketStatus.CheckedIn);

        var access = await Evaluate(context, customer, trip.Id);

        access.Allowed.ShouldBeTrue();
        access.ReasonCode.ShouldBe(TourGuideAccessReasons.Allowed);
        access.StartedAt.ShouldNotBeNull();
    }

    /// <summary>Hạn hiển thị lấy đúng hạn check-out của vé: giờ đến bến cuối + 10 + 5 phút ân hạn.</summary>
    [Test]
    public async Task AllowedAccessCarriesTheTicketDeadlineForTheCountdown()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customer = await SeatFlowTestData.SeedCustomerAsync(context);
        var trip = await SeedTripWithTicketAsync(context, customer.UserId!.Value, TicketStatus.CheckedIn);

        var access = await Evaluate(context, customer, trip.Id);

        access.ExpiresAt.ShouldBe(Departure.AddMinutes(50 + 15));
    }

    [Test]
    public async Task PassengerWhoHasNotCheckedInYetIsTurnedAway()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customer = await SeatFlowTestData.SeedCustomerAsync(context);
        var trip = await SeedTripWithTicketAsync(context, customer.UserId!.Value, TicketStatus.Active);

        var access = await Evaluate(context, customer, trip.Id);

        access.Allowed.ShouldBeFalse();
        access.ReasonCode.ShouldBe(TourGuideAccessReasons.NotCheckedIn);
    }

    [Test]
    public async Task PassengerWhoCheckedOutLosesAccess()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customer = await SeatFlowTestData.SeedCustomerAsync(context);
        var trip = await SeedTripWithTicketAsync(context, customer.UserId!.Value, TicketStatus.CheckedOut);

        var access = await Evaluate(context, customer, trip.Id);

        access.Allowed.ShouldBeFalse();
        access.ReasonCode.ShouldBe(TourGuideAccessReasons.CheckedOut);
    }

    /// <summary>
    /// Khách quên check-out: job dọn vé chuyển sang Expired, và cửa đóng theo — đây chính là
    /// đường đóng phiên cho ca hay gặp nhất (xuống bến cuối rồi đi thẳng).
    /// </summary>
    [Test]
    public async Task ExpiredTicketClosesTheSession()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customer = await SeatFlowTestData.SeedCustomerAsync(context);
        var trip = await SeedTripWithTicketAsync(context, customer.UserId!.Value, TicketStatus.Expired);

        var access = await Evaluate(context, customer, trip.Id);

        access.Allowed.ShouldBeFalse();
        access.ReasonCode.ShouldBe(TourGuideAccessReasons.SessionExpired);
    }

    [Test]
    public async Task SomeoneElsesTicketDoesNotOpenTheGuide()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var buyer = await SeatFlowTestData.SeedCustomerAsync(context);
        var stranger = await SeatFlowTestData.SeedCustomerAsync(context);
        var trip = await SeedTripWithTicketAsync(context, buyer.UserId!.Value, TicketStatus.CheckedIn);

        var access = await Evaluate(context, stranger, trip.Id);

        access.Allowed.ShouldBeFalse();
        access.ReasonCode.ShouldBe(TourGuideAccessReasons.NoTicket);
    }

    /// <summary>Vé chuyến này không mở được chuyến khác — kể cả khi đang check-in.</summary>
    [Test]
    public async Task TicketForAnotherTripDoesNotOpenThisOne()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customer = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedTripWithTicketAsync(context, customer.UserId!.Value, TicketStatus.CheckedIn);
        var otherTrip = await SeedTripAsync(context);

        var access = await Evaluate(context, customer, otherTrip.Id);

        access.Allowed.ShouldBeFalse();
        access.ReasonCode.ShouldBe(TourGuideAccessReasons.NoTicket);
    }

    /// <summary>Khứ hồi: chiều đi đã xuống tàu thì đóng, chiều về chưa check-in cũng đóng.</summary>
    [Test]
    public async Task RoundTripLegsAreJudgedSeparately()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customer = await SeatFlowTestData.SeedCustomerAsync(context);
        var outbound = await SeedTripAsync(context);
        var inbound = await SeedTripAsync(context, Departure.AddHours(6));
        var booking = NewBooking(customer.UserId!.Value, outbound);
        booking.ReturnTripId = inbound.Id;
        AddPassengerTicket(booking, outbound, TicketStatus.CheckedOut);
        AddPassengerTicket(booking, inbound, TicketStatus.Active);
        context.Add(booking);
        await context.SaveChangesAsync();

        (await Evaluate(context, customer, outbound.Id)).ReasonCode
            .ShouldBe(TourGuideAccessReasons.CheckedOut);
        (await Evaluate(context, customer, inbound.Id)).ReasonCode
            .ShouldBe(TourGuideAccessReasons.NotCheckedIn);
    }

    /// <summary>
    /// Ghim lỗ đã từng có: lọc vé ở mức BOOKING thì chiều đi đang CheckedIn sẽ mở luôn hướng dẫn
    /// viên cho chiều về — tức mở cho người còn chưa lên tàu, có khi còn chưa tới bến.
    /// </summary>
    [Test]
    public async Task CheckedInOutboundLegDoesNotOpenTheReturnLeg()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customer = await SeatFlowTestData.SeedCustomerAsync(context);
        var outbound = await SeedTripAsync(context);
        var inbound = await SeedTripAsync(context, Departure.AddHours(6));
        var booking = NewBooking(customer.UserId!.Value, outbound);
        booking.ReturnTripId = inbound.Id;
        AddPassengerTicket(booking, outbound, TicketStatus.CheckedIn);
        AddPassengerTicket(booking, inbound, TicketStatus.Active);
        context.Add(booking);
        await context.SaveChangesAsync();

        (await Evaluate(context, customer, outbound.Id)).Allowed
            .ShouldBeTrue("khách đang ngồi trên chuyến chiều đi");
        (await Evaluate(context, customer, inbound.Id)).Allowed
            .ShouldBeFalse("chiều về còn chưa check-in");
    }

    /// <summary>Admin phải mở được màn này không cần vé, nếu không thì không ai demo hay dò lỗi được.</summary>
    [Test]
    public async Task AdminGetsInWithoutATicket()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var trip = await SeedTripAsync(context);

        var access = await Evaluate(context, admin, trip.Id);

        access.Allowed.ShouldBeTrue();
    }

    /// <summary>Staff KHÔNG được bỏ qua cửa — muốn thử thì đăng nhập tài khoản quản trị.</summary>
    [Test]
    public async Task StaffWithoutTicketIsTurnedAwayLikeAnyoneElse()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staff = await SeatFlowTestData.SeedStaffAsync(context);
        var trip = await SeedTripAsync(context);

        var access = await Evaluate(context, staff, trip.Id);

        access.Allowed.ShouldBeFalse();
        access.ReasonCode.ShouldBe(TourGuideAccessReasons.NoTicket);
    }

    /// <summary>
    /// Bị chặn phải ném ĐÚNG kiểu mang theo lý do — 403 rỗng thì app không biết hiện câu nào và
    /// phải gọi thêm một vòng nữa chỉ để hỏi "vừa rồi vì sao".
    ///
    /// Responder truyền null có chủ ý: nếu cửa lọt, handler sẽ gọi model và nổ NullReference —
    /// tức test này cũng canh luôn việc KHÔNG được tiêu tiền LLM trước khi kiểm quyền.
    /// </summary>
    [Test]
    public async Task DeniedAskCarriesTheReasonCodeAndNeverReachesTheModel()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customer = await SeatFlowTestData.SeedCustomerAsync(context);
        var trip = await SeedTripWithTicketAsync(context, customer.UserId!.Value, TicketStatus.CheckedOut);
        var handler = new AskTourGuideTextCommandHandler(
            responder: null!,
            new TourGuideAccessSupport(context, customer));

        var exception = await Should.ThrowAsync<TourGuideAccessDeniedException>(() =>
            handler.Handle(
                new AskTourGuideTextCommand("Toa nha kia la gi?", TripId: trip.Id),
                CancellationToken.None));

        exception.ReasonCode.ShouldBe(TourGuideAccessReasons.CheckedOut);
    }

    private static Task<TourGuideAccess> Evaluate(
        ApplicationDbContext context,
        TestUserContext user,
        Guid tripId) =>
        new TourGuideAccessSupport(context, user).EvaluateAsync(tripId, CancellationToken.None);

    private static async Task<Trip> SeedTripWithTicketAsync(
        ApplicationDbContext context,
        Guid userId,
        TicketStatus ticketStatus)
    {
        var trip = await SeedTripAsync(context);
        var booking = NewBooking(userId, trip);
        AddPassengerTicket(booking, trip, ticketStatus);
        context.Add(booking);
        await context.SaveChangesAsync();
        return trip;
    }

    /// <summary>
    /// Ba bến, bến cuối KHÔNG có thời gian dừng — đúng như dữ liệu thật
    /// (<c>TripStopScheduleSupport.ResolveStayDurationMinutes</c> trả 0 cho bến đầu và bến cuối).
    /// </summary>
    private static async Task<Trip> SeedTripAsync(ApplicationDbContext context, DateTimeOffset? departure = null)
    {
        var departureTime = departure ?? Departure;
        var route = new Route
        {
            RouteCode = $"RT-{Guid.NewGuid():N}"[..20],
            RouteName = "Test Route"
        };
        var trip = new Trip
        {
            Route = route,
            TripCode = $"TR-{Guid.NewGuid():N}"[..20],
            OperatingDate = DateOnly.FromDateTime(departureTime.UtcDateTime),
            DepartureTime = departureTime,
            ArrivalTime = departureTime.AddMinutes(50),
            CapacitySnapshot = 1
        };

        for (var order = 1; order <= 3; order++)
        {
            trip.TripStops.Add(new TripStop
            {
                TripId = trip.Id,
                StationId = Guid.NewGuid(),
                StopOrder = order,
                StayDurationMinutes = order == 2 ? 5 : 0,
                PlannedArrivalTime = departureTime.AddMinutes(25 * (order - 1)),
                PlannedDepartureTime = order == 3 ? null : departureTime.AddMinutes(25 * (order - 1))
            });
        }

        context.Add(trip);
        await context.SaveChangesAsync();
        return trip;
    }

    private static Booking NewBooking(Guid userId, Trip trip) =>
        new()
        {
            UserId = userId,
            TripId = trip.Id,
            BookingCode = $"BK-{Guid.NewGuid():N}"[..20],
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            DepositAmount = 10000,
            RemainingAmount = 0
        };

    /// <summary>
    /// Hành khách KHÔNG ghi stop order — đúng dữ liệu của vé sightseeing, và cũng là ca bắt hạn
    /// hiển thị phải rơi về bến cuối.
    /// </summary>
    private static void AddPassengerTicket(Booking booking, Trip trip, TicketStatus status)
    {
        var passenger = new BookingPassenger
        {
            BookingId = booking.Id,
            FullName = "Nguyen Van A",
            TripId = trip.Id
        };
        booking.Passengers.Add(passenger);

        booking.Tickets.Add(new Ticket
        {
            BookingId = booking.Id,
            BookingPassenger = passenger,
            TicketCode = $"TK{Guid.NewGuid():N}"[..16],
            QrToken = Guid.NewGuid().ToString("N"),
            TicketStatus = status,
            IssuedAt = Departure.AddDays(-3),
            CheckedInAt = status is TicketStatus.CheckedIn or TicketStatus.CheckedOut or TicketStatus.Expired
                ? Departure.AddMinutes(-5)
                : null,
            CheckedOutAt = status == TicketStatus.CheckedOut ? Departure.AddMinutes(50) : null
        });
    }
}
