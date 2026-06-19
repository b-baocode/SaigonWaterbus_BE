using NUnit.Framework;
using SaigonWaterbus.Application.BookingHistory;
using SaigonWaterbus.Application.CustomBookingRequests;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.BookingHistory;

public class GetMyBookingHistoryQueryTests
{
    [Test]
    public async Task ReturnsCustomBookingsForOwnerOrContactUserWithStoredQrPayload()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var ownedQrToken = "owned-history-token";
        var owned = CustomBooking(
            userId,
            contactUserId: null,
            created: new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero));
        owned.Tickets.Add(Ticket(owned, ownedQrToken, CustomBookingTicketStatus.Active));

        var contacted = CustomBooking(
            userId: null,
            contactUserId: userId,
            created: new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.Zero));
        contacted.Tickets.Add(Ticket(contacted, qrToken: null, CustomBookingTicketStatus.Active));

        var unrelated = CustomBooking(
            Guid.NewGuid(),
            contactUserId: null,
            created: new DateTimeOffset(2026, 6, 18, 11, 0, 0, TimeSpan.Zero));
        unrelated.Tickets.Add(Ticket(unrelated, "other-token", CustomBookingTicketStatus.Active));

        context.AddRange(owned, contacted, unrelated);
        await context.SaveChangesAsync();

        var result = await new GetMyBookingHistoryQueryHandler(context, new TestUserContext(userId))
            .Handle(new GetMyBookingHistoryQuery(), CancellationToken.None);

        result.Select(x => x.Id).ShouldBe([contacted.Id, owned.Id]);
        result.Select(x => x.Type).ShouldAllBe(x => x == "CustomBooking");
        result.Single(x => x.Id == owned.Id).Ticket!.QrPayload
            .ShouldBe(CustomBookingTicketSupport.CreateQrPayload(ownedQrToken));
        result.Single(x => x.Id == contacted.Id).Ticket!.QrPayload.ShouldBeNull();
    }

    [Test]
    public async Task CustomBookingHistoryPrefersActiveTicketWhenMultipleTicketsExist()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var activeQrToken = "active-history-token";
        var booking = CustomBooking(
            userId,
            contactUserId: null,
            created: new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero));
        booking.Tickets.Add(Ticket(
            booking,
            "used-history-token",
            CustomBookingTicketStatus.Used,
            issuedAt: new DateTimeOffset(2026, 6, 18, 11, 0, 0, TimeSpan.Zero)));
        booking.Tickets.Add(Ticket(
            booking,
            activeQrToken,
            CustomBookingTicketStatus.Active,
            issuedAt: new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.Zero)));

        context.Add(booking);
        await context.SaveChangesAsync();

        var result = await new GetMyBookingHistoryQueryHandler(context, new TestUserContext(userId))
            .Handle(new GetMyBookingHistoryQuery(), CancellationToken.None);

        var item = result.Single();
        item.Ticket!.Status.ShouldBe(CustomBookingTicketStatus.Active);
        item.Ticket.QrPayload.ShouldBe(CustomBookingTicketSupport.CreateQrPayload(activeQrToken));
    }

    private static CustomBookingRequest CustomBooking(
        Guid? userId,
        Guid? contactUserId,
        DateTimeOffset created)
    {
        var service = new WaterbusService
        {
            Code = "WT",
            Name = "Water Taxi",
            BookingMode = BookingMode.VesselRental,
            IsActive = true
        };
        var fromStation = Station("BD", "Bach Dang");
        var toStation = Station("LD", "Linh Dong");
        var booking = new CustomBookingRequest
        {
            UserId = userId,
            ContactUserId = contactUserId,
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            WaterbusServiceId = service.Id,
            WaterbusService = service,
            RequestedNumberOfDecks = 1,
            RequestedSeatSetupType = SeatSetupType.FullStandard,
            RentalUnit = VesselRentalUnit.Hour,
            DepartureDate = new DateOnly(2026, 6, 20),
            PreferredStartTime = new TimeOnly(8, 0),
            FromStationId = fromStation.Id,
            FromStation = fromStation,
            FromLocation = fromStation.StationName,
            ToStationId = toStation.Id,
            ToStation = toStation,
            ToLocation = toStation.StationName,
            PassengerCount = 1,
            AdultCount = 1,
            ChildCount = 0,
            Status = CustomBookingRequestStatus.Confirmed,
            Created = created
        };
        booking.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = booking.Id,
            CustomBookingRequest = booking,
            QuotedPrice = 1_500_000m,
            DepositAmount = 750_000m,
            RemainingAmount = 750_000m,
            Currency = "VND",
            DepositPaymentStatus = CustomBookingDepositPaymentStatus.Paid,
            RemainingPaymentStatus = CustomBookingDepositPaymentStatus.Paid
        };

        return booking;
    }

    private static CustomBookingTicket Ticket(
        CustomBookingRequest booking,
        string? qrToken,
        CustomBookingTicketStatus status,
        DateTimeOffset? issuedAt = null)
    {
        return new CustomBookingTicket
        {
            CustomBookingRequestId = booking.Id,
            CustomBookingRequest = booking,
            TicketCode = $"CBT-{Guid.NewGuid():N}"[..12],
            QrToken = qrToken,
            QrTokenHash = CustomBookingTicketSupport.HashQrToken(qrToken ?? Guid.NewGuid().ToString("N")),
            QrIssuedAt = issuedAt ?? new DateTimeOffset(2026, 6, 18, 9, 30, 0, TimeSpan.Zero),
            Status = status
        };
    }

    private static Station Station(string code, string name) =>
        new()
        {
            StationCode = code,
            StationName = name,
            Status = StationStatus.Active
        };
}
