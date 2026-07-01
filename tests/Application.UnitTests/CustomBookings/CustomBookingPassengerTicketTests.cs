using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.CustomBookings;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CustomBookings;

public class CustomBookingPassengerTicketTests
{
    [Test]
    public async Task UpdatingPassengersCreatesTicketForEachPassenger()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = PaidCustomBooking(userId, adultCount: 2);
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = CreateUpdateHandler(context, userId);

        var result = await handler.Handle(
            new UpdateCustomBookingPassengersCommand(
                booking.Id,
                [
                    new CustomBookingPassengerRequest("Nguyen Van A", "1990-01-01"),
                    new CustomBookingPassengerRequest("Tran Thi B", "1992-02-02")
                ]),
            CancellationToken.None);

        result.RegisteredPassengerCount.ShouldBe(2);
        result.TicketCount.ShouldBe(2);
        result.Tickets.Count.ShouldBe(2);

        var tickets = context.Tickets.OrderBy(x => x.TicketCode).ToArray();
        tickets.Length.ShouldBe(2);
        tickets.All(x => x.BookingPassengerId.HasValue).ShouldBeTrue();
        tickets.Select(x => x.QrToken).Distinct().Count().ShouldBe(2);
        tickets.ShouldAllBe(x => x.TicketTypeCode == "CUSTOM_BOOKING");
    }

    [Test]
    public async Task ReplacingPassengersCancelsOldTicketsAndCreatesNewTickets()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = PaidCustomBooking(userId, adultCount: 2);
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = CreateUpdateHandler(context, userId);
        await handler.Handle(
            new UpdateCustomBookingPassengersCommand(
                booking.Id,
                [
                    new CustomBookingPassengerRequest("Nguyen Van A", "1990-01-01"),
                    new CustomBookingPassengerRequest("Tran Thi B", "1992-02-02")
                ]),
            CancellationToken.None);

        var firstTicketIds = context.Tickets.Select(x => x.Id).ToHashSet();

        await handler.Handle(
            new UpdateCustomBookingPassengersCommand(
                booking.Id,
                [
                    new CustomBookingPassengerRequest("Le Van C", "1988-03-03"),
                    new CustomBookingPassengerRequest("Pham Thi D", "1989-04-04")
                ]),
            CancellationToken.None);

        var tickets = context.Tickets.ToArray();
        tickets.Length.ShouldBe(4);
        tickets.Count(x => firstTicketIds.Contains(x.Id) && x.TicketStatus == TicketStatus.Cancelled)
            .ShouldBe(2);

        var activeTickets = tickets.Where(x => x.TicketStatus == TicketStatus.Active).ToArray();
        activeTickets.Length.ShouldBe(2);
        activeTickets.All(x => x.BookingPassengerId.HasValue).ShouldBeTrue();

        var currentPassengerIds = context.Set<BookingPassenger>().Select(x => x.Id).ToHashSet();
        activeTickets.Select(x => x.BookingPassengerId!.Value).ToHashSet()
            .SetEquals(currentPassengerIds)
            .ShouldBeTrue();
    }

    [Test]
    public async Task ExportTicketsReturnsPassengerTicketsForBookingOwner()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var user = Customer();
        var booking = PaidCustomBooking(user.Id, adultCount: 2);
        context.AddRange(user.Role, user, booking);
        await context.SaveChangesAsync();

        var updateHandler = CreateUpdateHandler(context, user.Id);
        await updateHandler.Handle(
            new UpdateCustomBookingPassengersCommand(
                booking.Id,
                [
                    new CustomBookingPassengerRequest("Nguyen Van A", "1990-01-01"),
                    new CustomBookingPassengerRequest("Tran Thi B", "1992-02-02")
                ]),
            CancellationToken.None);

        var exportHandler = new ExportCustomBookingTicketsQueryHandler(
            context,
            new TestUserContext(user.Id));

        var export = await exportHandler.Handle(
            new ExportCustomBookingTicketsQuery(booking.Id),
            CancellationToken.None);

        export.BookingCode.ShouldBe(booking.BookingCode);
        export.Tickets.Count.ShouldBe(2);
        export.Tickets.ShouldAllBe(x => x.PassengerId.HasValue);
        export.Tickets.ShouldAllBe(x => !string.IsNullOrWhiteSpace(x.QrToken));
    }

    [Test]
    public async Task ExportTicketsCanSelectSpecificTicketIds()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var user = Customer();
        var booking = PaidCustomBooking(user.Id, adultCount: 2);
        context.AddRange(user.Role, user, booking);
        await context.SaveChangesAsync();

        var updateHandler = CreateUpdateHandler(context, user.Id);
        await updateHandler.Handle(
            new UpdateCustomBookingPassengersCommand(
                booking.Id,
                [
                    new CustomBookingPassengerRequest("Nguyen Van A", "1990-01-01"),
                    new CustomBookingPassengerRequest("Tran Thi B", "1992-02-02")
                ]),
            CancellationToken.None);

        var selectedTicketId = context.Tickets
            .OrderBy(x => x.TicketCode)
            .Select(x => x.Id)
            .First();
        var exportHandler = new ExportCustomBookingTicketsQueryHandler(
            context,
            new TestUserContext(user.Id));

        var export = await exportHandler.Handle(
            new ExportCustomBookingTicketsQuery(booking.Id, [selectedTicketId]),
            CancellationToken.None);

        export.Tickets.Count.ShouldBe(1);
        export.Tickets.Single().TicketId.ShouldBe(selectedTicketId);
    }

    [Test]
    public async Task ExportTicketsRejectsInvalidTicketIds()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var user = Customer();
        var booking = PaidCustomBooking(user.Id, adultCount: 1);
        context.AddRange(user.Role, user, booking);
        await context.SaveChangesAsync();

        var updateHandler = CreateUpdateHandler(context, user.Id);
        await updateHandler.Handle(
            new UpdateCustomBookingPassengersCommand(
                booking.Id,
                [new CustomBookingPassengerRequest("Nguyen Van A", "1990-01-01")]),
            CancellationToken.None);

        var exportHandler = new ExportCustomBookingTicketsQueryHandler(
            context,
            new TestUserContext(user.Id));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            exportHandler.Handle(
                new ExportCustomBookingTicketsQuery(booking.Id, [Guid.NewGuid()]),
                CancellationToken.None));

        exception.Errors["ticketIds"].Single()
            .ShouldContain("không thuộc custom booking");
    }

    [Test]
    public async Task ManifestCanBeFoundByBookingCode()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var user = Customer();
        var station = new Station
        {
            StationCode = "BD",
            StationName = "Ben Bach Dang"
        };
        var booking = PaidCustomBooking(user.Id, adultCount: 2);
        booking.ItineraryStops.Add(new BookingItineraryStop
        {
            Booking = booking,
            Station = station,
            StationId = station.Id,
            StopOrder = 1,
            StayDurationMinutes = 30,
            Note = "Don khach"
        });
        context.AddRange(user.Role, user, station, booking);
        await context.SaveChangesAsync();

        var updateHandler = CreateUpdateHandler(context, user.Id);
        await updateHandler.Handle(
            new UpdateCustomBookingPassengersCommand(
                booking.Id,
                [
                    new CustomBookingPassengerRequest("Nguyen Van A", "1990-01-01"),
                    new CustomBookingPassengerRequest("Tran Thi B", "1992-02-02")
                ]),
            CancellationToken.None);

        var manifestHandler = new GetCustomBookingManifestByCodeQueryHandler(
            context,
            new TestUserContext(user.Id));

        var manifest = await manifestHandler.Handle(
            new GetCustomBookingManifestByCodeQuery(booking.BookingCode.ToLowerInvariant()),
            CancellationToken.None);

        manifest.BookingId.ShouldBe(booking.Id);
        manifest.BookingCode.ShouldBe(booking.BookingCode);
        manifest.RegisteredPassengerCount.ShouldBe(2);
        manifest.TicketSummary.TotalTickets.ShouldBe(2);
        manifest.TicketSummary.ActiveTickets.ShouldBe(2);
        manifest.ItineraryStops.Single().StationName.ShouldBe("Ben Bach Dang");
        manifest.Passengers.Count.ShouldBe(2);
        manifest.Passengers.ShouldAllBe(x => x.TicketId.HasValue);
        manifest.Passengers.ShouldAllBe(x => x.CanCheckIn);
    }

    private static UpdateCustomBookingPassengersCommandHandler CreateUpdateHandler(
        IApplicationDbContext context,
        Guid userId) =>
        new(
            context,
            new TestUserContext(userId),
            new TestPaymentNotificationSender(),
            TimeProvider.System);

    private static Booking PaidCustomBooking(Guid userId, int adultCount) =>
        new()
        {
            BookingType = Booking.CustomBookingType,
            UserId = userId,
            BookingCode = $"CB{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = adultCount,
            PassengerCount = adultCount,
            SubtotalAmount = 1_000_000,
            TotalAmount = 1_000_000,
            DepositAmount = 1_000_000,
            RemainingAmount = 0
        };

    private static User Customer()
    {
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };

        return new User
        {
            FullName = "Customer",
            RoleId = role.Id,
            Role = role,
            Status = UserStatus.Active
        };
    }

    private sealed class TestPaymentNotificationSender : IPaymentNotificationSender
    {
        public Task SendPaymentSucceededAsync(
            PaymentSucceededNotification notification,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SendBoardingPassAsync(
            BoardingPassNotification notification,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
