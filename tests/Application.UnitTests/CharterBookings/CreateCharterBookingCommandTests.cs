using NUnit.Framework;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class CreateCharterBookingCommandTests
{
    [Test]
    public async Task CreateStoresRequestedBoatsAndReturnsThemInDetail()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            PhoneNumber = "0900000000",
            Email = "customer@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        context.AddRange(role, user);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedBookingCodeGenerator("CB0001"),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(
            new CreateCharterBookingCommand(
                new DateOnly(2026, 7, 20),
                BoatRentalUnit.Day,
                1,
                AdultCount: 10,
                ChildCount: 2,
                RequestedBoats:
                [
                    new CreateCharterBookingBoatRequest(1),
                    new CreateCharterBookingBoatRequest(2)
                ],
                ContactName: "Nguyen Van B",
                ContactPhone: "0911111111",
                ContactEmail: "booking-contact@example.test"),
            CancellationToken.None);

        result.RequestedBoatCount.ShouldBe(2);
        result.RequestedBoats.Select(x => x.NumberOfDecks)
            .ShouldBe([1, 2]);

        var booking = context.Set<Booking>().Single(x => x.Id == result.BookingId);
        booking.RequestedBoatCount.ShouldBe(2);
        booking.RequestedBoatDecks.ShouldBe("1,2");
        booking.RequestedBoatTypes.ShouldBeNull();
        booking.PreferredSeatSetupType.ShouldBeNull();
        booking.PromotionId.ShouldBeNull();
        booking.ContactName.ShouldBe("Nguyen Van B");
        booking.ContactPhone.ShouldBe("0911111111");
        booking.ContactEmail.ShouldBe("booking-contact@example.test");

        var detail = await new GetCharterBookingDetailQueryHandler(
                context,
                new TestUserContext(user.Id))
            .Handle(new GetCharterBookingDetailQuery(result.BookingId), CancellationToken.None);

        detail.RequestedBoatCount.ShouldBe(2);
        detail.RequestedBoats.Select(x => x.NumberOfDecks)
            .ShouldBe([1, 2]);
        detail.ContactName.ShouldBe("Nguyen Van B");
        detail.ContactPhone.ShouldBe("0911111111");
        detail.ContactEmail.ShouldBe("booking-contact@example.test");
    }

    [Test]
    public async Task CreateRequiresPhoneAfterAccountFallback()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            Email = "customer@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        context.AddRange(role, user);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedBookingCodeGenerator("CB0002"),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero)));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new CreateCharterBookingCommand(
                    new DateOnly(2026, 7, 20),
                    BoatRentalUnit.Day,
                    1,
                    AdultCount: 10,
                    ChildCount: 2,
                    ContactEmail: "customer@example.test"),
                CancellationToken.None));

        exception.Errors["contactPhone"].Single()
            .ShouldContain("Số điện thoại");
    }

    [Test]
    public async Task ListReturnsCustomerSummaryFieldsAndCharterBookingCodePrefix()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            PhoneNumber = "0900000000",
            Email = "customer@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        var fromStation = new Station
        {
            StationCode = "ST-A",
            StationName = "Bến A",
            Status = StationStatus.Active
        };
        var toStation = new Station
        {
            StationCode = "ST-B",
            StationName = "Bến B",
            Status = StationStatus.Active
        };
        context.AddRange(role, user, fromStation, toStation);
        await context.SaveChangesAsync();

        var createHandler = new CreateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedBookingCodeGenerator("BK-20260705-ABCDE"),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero)));

        var result = await createHandler.Handle(
            new CreateCharterBookingCommand(
                new DateOnly(2026, 7, 20),
                BoatRentalUnit.Day,
                1,
                AdultCount: 10,
                ChildCount: 2,
                StartTime: new TimeOnly(8, 4),
                FromStationId: fromStation.Id,
                ToStationId: toStation.Id,
                RequestedBoats:
                [
                    new CreateCharterBookingBoatRequest(1)
                ]),
            CancellationToken.None);

        result.BookingCode.ShouldBe("CB-20260705-ABCDE");

        var list = await new GetCharterBookingListQueryHandler(
                context,
                new TestUserContext(user.Id))
            .Handle(new GetCharterBookingListQuery(), CancellationToken.None);

        var item = list.Single();
        item.Id.ShouldBe(result.BookingId);
        item.BookingCode.ShouldBe("CB-20260705-ABCDE");
        item.BookingStatus.ShouldBe("PendingQuote");
        item.PaymentStatus.ShouldBe("Unpaid");
        item.DepartureDate.ShouldBe("2026-07-20");
        item.StartTime.ShouldBe("08:04:00");
        item.RentalUnit.ShouldBe("Day");
        item.DurationValue.ShouldBe(1);
        item.AdultCount.ShouldBe(10);
        item.ChildCount.ShouldBe(2);
        item.PassengerCount.ShouldBe(12);
        item.FromStationName.ShouldBe("Bến A");
        item.ToStationName.ShouldBe("Bến B");
        item.BoatName.ShouldBeNull();
        item.SubtotalAmount.ShouldBeNull();
        item.FinalAmount.ShouldBeNull();
        item.RequestedBoats.Select(x => x.NumberOfDecks).ShouldBe([1]);
    }

    [Test]
    public async Task UpdateStoresContactInfoAndDoesNotRevalidateUnchangedDepartureDate()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            PhoneNumber = "0900000000",
            Email = "account@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = "CB-EDIT-001",
            User = user,
            UserId = user.Id,
            ContactName = user.FullName,
            ContactPhone = user.PhoneNumber!,
            ContactEmail = "receive@example.test",
            DepartureDate = new DateOnly(2026, 7, 10),
            StartTime = new TimeOnly(8, 0),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            ChildCount = 1,
            PassengerCount = 2,
            RequestedBoatCount = 1,
            RequestedBoatDecks = "1",
            BookingStatus = BookingStatus.PendingQuote,
            PaymentStatus = "Unpaid"
        };
        context.AddRange(role, user, booking);
        await context.SaveChangesAsync();

        var handler = new UpdateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero)));

        var detail = await handler.Handle(
            new UpdateCharterBookingCommand(
                booking.Id,
                new DateOnly(2026, 7, 10),
                BoatRentalUnit.Day,
                2,
                AdultCount: 2,
                ChildCount: 1,
                StartTime: new TimeOnly(9, 30),
                RequestedBoats: [new CreateCharterBookingBoatRequest(2)],
                ContactName: "Updated customer",
                ContactPhone: "0988888888",
                ContactEmail: "updated@example.test"),
            CancellationToken.None);

        detail.ContactName.ShouldBe("Updated customer");
        detail.ContactPhone.ShouldBe("0988888888");
        detail.ContactEmail.ShouldBe("updated@example.test");
        detail.DepartureDate.ShouldBe(new DateOnly(2026, 7, 10));
        detail.StartTime.ShouldBe(new TimeOnly(9, 30));
        detail.DurationValue.ShouldBe(2);
        detail.PassengerCount.ShouldBe(3);

        var savedBooking = context.Set<Booking>().Single(x => x.Id == booking.Id);
        savedBooking.ContactName.ShouldBe("Updated customer");
        savedBooking.ContactPhone.ShouldBe("0988888888");
        savedBooking.ContactEmail.ShouldBe("updated@example.test");
        savedBooking.RequestedBoatDecks.ShouldBe("2");
        savedBooking.RequestedBoatTypes.ShouldBeNull();
    }

    [Test]
    public async Task UpdatePreservesExistingValuesWhenFieldsAreNotSubmitted()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            PhoneNumber = "0900000000",
            Email = "account@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        var fromStation = new Station
        {
            StationCode = "ST-FROM",
            StationName = "Bến đi",
            Status = StationStatus.Active
        };
        var toStation = new Station
        {
            StationCode = "ST-TO",
            StationName = "Bến đến",
            Status = StationStatus.Active
        };
        var stopStation = new Station
        {
            StationCode = "ST-STOP",
            StationName = "Bến dừng",
            Status = StationStatus.Active
        };
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = "CB-EDIT-KEEP",
            User = user,
            UserId = user.Id,
            ContactName = user.FullName,
            ContactPhone = user.PhoneNumber!,
            ContactEmail = "receive@example.test",
            FromStation = fromStation,
            FromStationId = fromStation.Id,
            ToStation = toStation,
            ToStationId = toStation.Id,
            DepartureDate = new DateOnly(2026, 7, 10),
            StartTime = new TimeOnly(8, 0),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            ChildCount = 0,
            PassengerCount = 1,
            RequestedBoatCount = 1,
            RequestedBoatDecks = "1",
            BoatRequirements = "Old boat note",
            SpecialRequests = "Old special note",
            BookingStatus = BookingStatus.PendingQuote,
            PaymentStatus = "Unpaid"
        };
        booking.ItineraryStops.Add(new BookingItineraryStop
        {
            Booking = booking,
            BookingId = booking.Id,
            Station = stopStation,
            StationId = stopStation.Id,
            StopOrder = 1,
            StayDurationMinutes = 15,
            Note = "Old stop note"
        });
        context.AddRange(role, user, fromStation, toStation, stopStation, booking);
        await context.SaveChangesAsync();

        var handler = new UpdateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero)));

        var detail = await handler.Handle(
            new UpdateCharterBookingCommand(
                booking.Id,
                SpecialRequests: "New special note"),
            CancellationToken.None);

        detail.DepartureDate.ShouldBe(new DateOnly(2026, 7, 10));
        detail.StartTime.ShouldBe(new TimeOnly(8, 0));
        detail.FromStationId.ShouldBe(fromStation.Id);
        detail.ToStationId.ShouldBe(toStation.Id);
        detail.DurationValue.ShouldBe(1);
        detail.PassengerCount.ShouldBe(1);
        detail.RequestedBoatCount.ShouldBe(1);
        detail.RequestedBoats.Single().NumberOfDecks.ShouldBe(1);
        detail.SpecialRequests.ShouldBe("New special note");
        detail.ItineraryStops.Single().StationId.ShouldBe(stopStation.Id);
        detail.ItineraryStops.Single().StayDurationMinutes.ShouldBe(15);
        detail.ItineraryStops.Single().Note.ShouldBe("Old stop note");

        var savedBooking = context.Set<Booking>().Include(x => x.ItineraryStops).Single(x => x.Id == booking.Id);
        savedBooking.RequestedBoatDecks.ShouldBe("1");
        savedBooking.BoatRequirements.ShouldBe("Old boat note");
        savedBooking.SpecialRequests.ShouldBe("New special note");
        savedBooking.ItineraryStops.Count.ShouldBe(1);
    }

    private sealed class FixedBookingCodeGenerator(string bookingCode) : IBookingCodeGenerator
    {
        public Task<string> GenerateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(bookingCode);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
