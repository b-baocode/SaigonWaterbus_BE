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
