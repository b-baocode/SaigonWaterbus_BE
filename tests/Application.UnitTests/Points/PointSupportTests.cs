using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.UnitTests.Points;

public class PointSupportTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);

    [TestCase(25_249, 252)]
    [TestCase(25_250, 253)]
    [TestCase(25_286, 253)]
    public void CalculateEarnedPointsRoundsToNearestWholePoint(decimal paidAmount, int expectedPoints)
    {
        PointSupport.CalculateEarnedPoints(paidAmount).ShouldBe(expectedPoints);
    }

    [TestCase(100_000, 120_000, 100_000)]
    [TestCase(100_000, 60_000, 60_000)]
    [TestCase(100_000, 0, 0)]
    [TestCase(100_000.75, 120_000, 100_000)]
    public void CalculateMaxRedeemablePointsUsesUpToFullBillAndPreservesExcessBalance(
        decimal billAmount,
        int pointBalance,
        int expectedPoints)
    {
        PointSupport.CalculateMaxRedeemablePoints(billAmount, pointBalance).ShouldBe(expectedPoints);
    }

    [Test]
    public async Task CompletingTripAwardsOnePercentOfNetPaidAmountOnce()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var user = await context.Set<User>().SingleAsync(u => u.Id == userContext.UserId);
        var trip = Trip("TR-POINT-1", TripStatus.Completed);
        var booking = Booking(user.Id, trip, "BK-POINT-1");
        var payment = PaidPayment(booking, 123_456m, refundAmount: 3_456m);
        context.AddRange(trip, booking, payment);
        await context.SaveChangesAsync();

        await PointSupport.AwardCompletionPointsForCompletedTripAsync(
            context,
            trip,
            TripStatus.InProgress,
            Now,
            CancellationToken.None);
        await context.SaveChangesAsync();

        user.PointBalance.ShouldBe(1_200);
        booking.PointsEarned.ShouldBe(1_200);
        var transaction = await context.Set<PointTransaction>().SingleAsync();
        transaction.TransactionType.ShouldBe(PointTransactionTypes.Earn);
        transaction.Points.ShouldBe(1_200);
        transaction.BalanceAfter.ShouldBe(1_200);
        transaction.BookingId.ShouldBe(booking.Id);

        await PointSupport.AwardCompletionPointsForCompletedTripAsync(
            context,
            trip,
            TripStatus.InProgress,
            Now.AddMinutes(1),
            CancellationToken.None);
        await context.SaveChangesAsync();

        user.PointBalance.ShouldBe(1_200);
        booking.PointsEarned.ShouldBe(1_200);
        (await context.Set<PointTransaction>().CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task RoundTripAwardsOnlyAfterBothTripsCompleted()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var user = await context.Set<User>().SingleAsync(u => u.Id == userContext.UserId);
        var outbound = Trip("TR-POINT-OUT", TripStatus.Completed);
        var inbound = Trip("TR-POINT-IN", TripStatus.Scheduled);
        var booking = Booking(user.Id, outbound, "BK-POINT-ROUND");
        booking.ReturnTrip = inbound;
        booking.ReturnTripId = inbound.Id;
        var payment = PaidPayment(booking, 100_000m);
        context.AddRange(outbound, inbound, booking, payment);
        await context.SaveChangesAsync();

        await PointSupport.AwardCompletionPointsForCompletedTripAsync(
            context,
            outbound,
            TripStatus.InProgress,
            Now,
            CancellationToken.None);
        await context.SaveChangesAsync();

        user.PointBalance.ShouldBe(0);
        booking.PointsEarned.ShouldBe(0);
        (await context.Set<PointTransaction>().CountAsync()).ShouldBe(0);

        inbound.TripStatus = TripStatus.Completed;
        await PointSupport.AwardCompletionPointsForCompletedTripAsync(
            context,
            inbound,
            TripStatus.InProgress,
            Now.AddHours(1),
            CancellationToken.None);
        await context.SaveChangesAsync();

        user.PointBalance.ShouldBe(1_000);
        booking.PointsEarned.ShouldBe(1_000);
        var transaction = await context.Set<PointTransaction>().SingleAsync();
        transaction.Points.ShouldBe(1_000);
        transaction.BookingId.ShouldBe(booking.Id);
    }

    [Test]
    public async Task CompletingTripDoesNotAwardPointsForNonCustomerOwner()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var staff = await context.Set<User>().SingleAsync(u => u.Id == staffContext.UserId);
        var trip = Trip("TR-POINT-STAFF", TripStatus.Completed);
        var booking = Booking(staff.Id, trip, "BK-POINT-STAFF");
        var payment = PaidPayment(booking, 100_000m);
        context.AddRange(trip, booking, payment);
        await context.SaveChangesAsync();

        await PointSupport.AwardCompletionPointsForCompletedTripAsync(
            context,
            trip,
            TripStatus.InProgress,
            Now,
            CancellationToken.None);
        await context.SaveChangesAsync();

        staff.PointBalance.ShouldBe(0);
        booking.PointsEarned.ShouldBe(0);
        (await context.Set<PointTransaction>().CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task NonCustomerOwnerCannotUsePointsForCheckout()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var staff = await context.Set<User>().SingleAsync(u => u.Id == staffContext.UserId);
        staff.PointBalance = 10_000;
        var trip = Trip("TR-POINT-REDEEM-STAFF", TripStatus.Scheduled);
        var booking = Booking(staff.Id, trip, "BK-POINT-REDEEM-STAFF");
        context.AddRange(trip, booking);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            PaymentSupport.ApplyPointsForCheckoutAsync(
                context,
                booking,
                5_000,
                Now,
                CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(m => m.Contains("Chỉ tài khoản khách hàng mới được dùng điểm"));
        staff.PointBalance.ShouldBe(10_000);
        booking.PointsUsed.ShouldBe(0);
    }

    [Test]
    public async Task CustomerCanPayFullBillWithPointsAndKeepsExcessBalance()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var customer = await context.Set<User>().SingleAsync(u => u.Id == customerContext.UserId);
        customer.PointBalance = 120_000;
        var trip = Trip("TR-POINT-FULL-BILL", TripStatus.Scheduled);
        var booking = Booking(customer.Id, trip, "BK-POINT-FULL-BILL");
        booking.BookingStatus = BookingStatus.PendingPayment;
        booking.PaymentStatus = "Unpaid";
        context.AddRange(trip, booking);
        await context.SaveChangesAsync();

        await PaymentSupport.ApplyPointsForCheckoutAsync(
            context,
            booking,
            pointsToUse: 100_000,
            Now,
            CancellationToken.None);

        booking.PointsUsed.ShouldBe(100_000);
        booking.TotalAmount.ShouldBe(0m);
        booking.RemainingAmount.ShouldBe(0m);
        customer.PointBalance.ShouldBe(20_000);
        var transaction = await context.Set<PointTransaction>().SingleAsync();
        transaction.TransactionType.ShouldBe(PointTransactionTypes.Redeem);
        transaction.Points.ShouldBe(-100_000);
        transaction.BalanceAfter.ShouldBe(20_000);
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

    private static Booking Booking(Guid userId, Trip trip, string code) => new()
    {
        UserId = userId,
        Trip = trip,
        TripId = trip.Id,
        BookingCode = code,
        ContactName = "Nguyen Van A",
        ContactPhone = "0900000000",
        BookingStatus = BookingStatus.Confirmed,
        PaymentStatus = "Paid",
        SubtotalAmount = 100_000m,
        TotalAmount = 100_000m,
        DepositAmount = 100_000m,
        RemainingAmount = 0
    };

    private static Payment PaidPayment(Booking booking, decimal amount, decimal refundAmount = 0m) => new()
    {
        Booking = booking,
        BookingId = booking.Id,
        PaymentCode = $"PM-{booking.BookingCode}",
        Amount = amount,
        RefundAmount = refundAmount,
        PaymentStatus = "Paid",
        PaidAt = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)
    };
}
