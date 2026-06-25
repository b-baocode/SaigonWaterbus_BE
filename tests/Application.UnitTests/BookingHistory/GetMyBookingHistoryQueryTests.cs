using NUnit.Framework;
using SaigonWaterbus.Application.BookingHistory;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.BookingHistory;

public class GetMyBookingHistoryQueryTests
{
    [Test]
    public async Task ReturnsEmptyWhenUserHasNoBookings()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();

        var result = await new GetMyBookingHistoryQueryHandler(context, new TestUserContext(userId))
            .Handle(new GetMyBookingHistoryQuery(), CancellationToken.None);

        result.ShouldBeEmpty();
    }
}
