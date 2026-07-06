using NUnit.Framework;
using SaigonWaterbus.Application.CharterBookings;
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
                    new CreateCharterBookingBoatRequest(SeatSetupType.StandardAndVip),
                    new CreateCharterBookingBoatRequest(SeatSetupType.FullStandard)
                ]),
            CancellationToken.None);

        result.RequestedBoatCount.ShouldBe(2);
        result.RequestedBoats.Select(x => x.SeatSetupType)
            .ShouldBe(["StandardAndVip", "FullStandard"]);

        var booking = context.Set<Booking>().Single(x => x.Id == result.BookingId);
        booking.RequestedBoatCount.ShouldBe(2);
        booking.RequestedBoatTypes.ShouldBe("StandardAndVip,FullStandard");
        booking.PreferredSeatSetupType.ShouldBe(SeatSetupType.StandardAndVip);
        booking.PromotionId.ShouldBeNull();

        var detail = await new GetCharterBookingDetailQueryHandler(
                context,
                new TestUserContext(user.Id))
            .Handle(new GetCharterBookingDetailQuery(result.BookingId), CancellationToken.None);

        detail.RequestedBoatCount.ShouldBe(2);
        detail.RequestedBoats.Select(x => x.SeatSetupType)
            .ShouldBe(["StandardAndVip", "FullStandard"]);
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
                    new CreateCharterBookingBoatRequest(SeatSetupType.FullStandard)
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
        item.RequestedBoats.Select(x => x.SeatSetupType).ShouldBe(["FullStandard"]);
    }

    [Test]
    public async Task UpdatePreservesContactEmailAndDoesNotRevalidateUnchangedDepartureDate()
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
            RequestedBoatTypes = "FullStandard",
            PreferredSeatSetupType = SeatSetupType.FullStandard,
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
                RequestedBoats: [new CreateCharterBookingBoatRequest(SeatSetupType.FullStandard)],
                ContactEmail: null),
            CancellationToken.None);

        detail.ContactEmail.ShouldBe("receive@example.test");
        detail.DepartureDate.ShouldBe(new DateOnly(2026, 7, 10));
        detail.StartTime.ShouldBe(new TimeOnly(9, 30));
        detail.DurationValue.ShouldBe(2);
        detail.PassengerCount.ShouldBe(3);

        var savedBooking = context.Set<Booking>().Single(x => x.Id == booking.Id);
        savedBooking.ContactEmail.ShouldBe("receive@example.test");
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
