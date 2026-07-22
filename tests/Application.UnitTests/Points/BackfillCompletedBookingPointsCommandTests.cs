using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Points;

public class BackfillCompletedBookingPointsCommandTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task BackfillAwardsCompletedSeatAndCharterBookingsOnce()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var user = await context.Set<User>().SingleAsync(x => x.Id == userContext.UserId);
        var trip = Trip("TR-BACKFILL-1", TripStatus.Completed);
        var seatBooking = CreateBooking(user.Id, "BK-BACKFILL-SEAT", Booking.SeatBookingType, BookingStatus.Confirmed, trip);
        var charterBooking = CreateBooking(user.Id, "BK-BACKFILL-CHARTER", Booking.CharterBookingType, BookingStatus.Completed);
        context.AddRange(
            trip,
            seatBooking,
            charterBooking,
            PaidPayment(seatBooking, 200_000m),
            PaidPayment(charterBooking, 50_000m));
        await context.SaveChangesAsync();

        var handler = new BackfillCompletedBookingPointsCommandHandler(
            context,
            new FixedTimeProvider(Now));
        var result = await handler.Handle(new BackfillCompletedBookingPointsCommand(), CancellationToken.None);

        result.CandidateBookingCount.ShouldBe(2);
        result.AwardedBookingCount.ShouldBe(2);
        result.SkippedBookingCount.ShouldBe(0);
        result.TotalPointsAwarded.ShouldBe(2_500);
        user.PointBalance.ShouldBe(2_500);
        seatBooking.PointsEarned.ShouldBe(2_000);
        charterBooking.PointsEarned.ShouldBe(500);
        (await context.Set<PointTransaction>().CountAsync()).ShouldBe(2);

        var rerun = await handler.Handle(new BackfillCompletedBookingPointsCommand(), CancellationToken.None);

        rerun.CandidateBookingCount.ShouldBe(0);
        rerun.AwardedBookingCount.ShouldBe(0);
        rerun.TotalPointsAwarded.ShouldBe(0);
        user.PointBalance.ShouldBe(2_500);
        (await context.Set<PointTransaction>().CountAsync()).ShouldBe(2);
    }

    [Test]
    public async Task BackfillWaitsUntilEveryConfirmedRoundTripLegIsCompleted()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var user = await context.Set<User>().SingleAsync(x => x.Id == userContext.UserId);
        var outbound = Trip("TR-BACKFILL-OUT", TripStatus.Completed);
        var inbound = Trip("TR-BACKFILL-IN", TripStatus.Scheduled);
        var booking = CreateBooking(user.Id, "BK-BACKFILL-ROUND", Booking.SeatBookingType, BookingStatus.Confirmed, outbound);
        booking.ReturnTrip = inbound;
        booking.ReturnTripId = inbound.Id;
        context.AddRange(outbound, inbound, booking, PaidPayment(booking, 100_000m));
        await context.SaveChangesAsync();

        var handler = new BackfillCompletedBookingPointsCommandHandler(
            context,
            new FixedTimeProvider(Now));
        var beforeReturnCompleted = await handler.Handle(
            new BackfillCompletedBookingPointsCommand(),
            CancellationToken.None);

        beforeReturnCompleted.CandidateBookingCount.ShouldBe(0);
        beforeReturnCompleted.AwardedBookingCount.ShouldBe(0);
        user.PointBalance.ShouldBe(0);
        booking.PointsEarned.ShouldBe(0);

        inbound.TripStatus = TripStatus.Completed;
        await context.SaveChangesAsync();
        var afterReturnCompleted = await handler.Handle(
            new BackfillCompletedBookingPointsCommand(),
            CancellationToken.None);

        afterReturnCompleted.CandidateBookingCount.ShouldBe(1);
        afterReturnCompleted.AwardedBookingCount.ShouldBe(1);
        afterReturnCompleted.TotalPointsAwarded.ShouldBe(1_000);
        user.PointBalance.ShouldBe(1_000);
        booking.PointsEarned.ShouldBe(1_000);
    }

    private static Trip Trip(string code, TripStatus status) => new()
    {
        TripCode = code,
        TripStatus = status,
        OperatingDate = new DateOnly(2030, 1, 1),
        DepartureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero),
        ArrivalTime = new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero),
        CapacitySnapshot = 20,
        Route = new Route
        {
            RouteCode = $"{code}-R",
            RouteName = $"{code} route"
        }
    };

    private static Booking CreateBooking(
        Guid userId,
        string code,
        string bookingType,
        BookingStatus bookingStatus,
        Trip? trip = null) => new()
    {
        UserId = userId,
        Trip = trip,
        TripId = trip?.Id,
        BookingType = bookingType,
        BookingCode = code,
        ContactName = "Nguyen Van A",
        ContactPhone = "0900000000",
        BookingStatus = bookingStatus,
        PaymentStatus = "Paid",
        SubtotalAmount = 100_000m,
        TotalAmount = 100_000m,
        DepositAmount = 100_000m,
        RemainingAmount = 0
    };

    private static Payment PaidPayment(Booking booking, decimal amount) => new()
    {
        Booking = booking,
        BookingId = booking.Id,
        PaymentCode = $"PM-{booking.BookingCode}",
        Amount = amount,
        PaymentStatus = "Paid",
        PaidAt = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)
    };
}
