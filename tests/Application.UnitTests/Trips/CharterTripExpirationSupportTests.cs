using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Trips;

public class CharterTripExpirationSupportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    [TestCase(TripStatus.Completed)]
    [TestCase(TripStatus.Cancelled)]
    public async Task DeletesTerminalTripAfterRetentionButKeepsBookingTicketAndRevenue(TripStatus status)
    {
        await using var context = SeatFlowTestData.CreateContext();
        var (trip, booking, ticket, payment) = await SeedPaidCharterAsync(
            context,
            status,
            Now.Subtract(CharterTripExpirationSupport.DeleteGracePeriod).AddMinutes(-1));

        var result = await CharterTripExpirationSupport.CompleteAndDeleteOverdueCharterTripsAsync(
            context,
            Now,
            CancellationToken.None);

        result.Deleted.ShouldBe(1);
        context.Set<Trip>().Any(x => x.Id == trip.Id).ShouldBeFalse();
        context.Set<Booking>().Any(x => x.Id == booking.Id).ShouldBeTrue();
        context.Set<Ticket>().Any(x => x.Id == ticket.Id).ShouldBeTrue();
        context.Set<Payment>().Any(x => x.Id == payment.Id && x.PaymentStatus == PaymentSupport.PaidStatus)
            .ShouldBeTrue();
    }

    [Test]
    public async Task KeepsCompletedTripUntilRetentionHasElapsed()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var (trip, _, _, _) = await SeedPaidCharterAsync(
            context,
            TripStatus.Completed,
            Now.Subtract(CharterTripExpirationSupport.DeleteGracePeriod).AddMinutes(1));

        var result = await CharterTripExpirationSupport.CompleteAndDeleteOverdueCharterTripsAsync(
            context,
            Now,
            CancellationToken.None);

        result.Deleted.ShouldBe(0);
        context.Set<Trip>().Any(x => x.Id == trip.Id).ShouldBeTrue();
    }

    [Test]
    public async Task CompletesOverdueTripBeforeStartingItsRetentionWindow()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var (trip, _, ticket, _) = await SeedPaidCharterAsync(
            context,
            TripStatus.Scheduled,
            statusChangedAt: null,
            departureTime: Now.Subtract(CharterTripExpirationSupport.ExpirationGracePeriod).AddMinutes(-1));

        var result = await CharterTripExpirationSupport.CompleteAndDeleteOverdueCharterTripsAsync(
            context,
            Now,
            CancellationToken.None);

        result.Completed.ShouldBe(1);
        result.Deleted.ShouldBe(0);
        trip.TripStatus.ShouldBe(TripStatus.Completed);
        trip.LastStatusChangedAt.ShouldBe(Now);
        ticket.TicketStatus.ShouldBe(TicketStatus.Expired);
    }

    private static async Task<(Trip Trip, Booking Booking, Ticket Ticket, Payment Payment)> SeedPaidCharterAsync(
        Infrastructure.Data.ApplicationDbContext context,
        TripStatus status,
        DateTimeOffset? statusChangedAt,
        DateTimeOffset? departureTime = null)
    {
        var departure = departureTime ?? Now.AddDays(-2);
        var route = new Route
        {
            RouteCode = $"R-{Guid.NewGuid():N}"[..20],
            RouteName = "Charter cleanup route",
            RouteType = RouteTypes.Charter,
            IsBookable = false
        };
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = $"CB-{Guid.NewGuid():N}"[..20],
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = status == TripStatus.Cancelled ? BookingStatus.Cancelled : BookingStatus.Completed,
            PaymentStatus = PaymentSupport.PaidBookingPaymentStatus,
            SubtotalAmount = 1_000_000m,
            TotalAmount = 1_000_000m,
            RemainingAmount = 0m
        };
        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            SourceBookingId = booking.Id,
            TripCode = $"TR-{Guid.NewGuid():N}"[..20],
            TripType = TripTypes.Charter,
            OperatingDate = DateOnly.FromDateTime(departure.UtcDateTime),
            DepartureTime = departure,
            ArrivalTime = departure.AddHours(2),
            CapacitySnapshot = 10,
            TripStatus = status,
            LastStatusChangedAt = statusChangedAt
        };
        booking.Trip = trip;
        booking.TripId = trip.Id;

        var ticket = new Ticket
        {
            Booking = booking,
            BookingId = booking.Id,
            TicketCode = $"TK-{Guid.NewGuid():N}"[..20],
            QrToken = $"QR-{Guid.NewGuid():N}",
            TicketStatus = TicketStatus.Active,
            IssuedAt = Now.AddDays(-3)
        };
        var payment = new Payment
        {
            Booking = booking,
            BookingId = booking.Id,
            PaymentCode = Random.Shared.NextInt64(1_000_000, 9_999_999).ToString(),
            Provider = PaymentSupport.PayOsProvider,
            Amount = booking.TotalAmount,
            Currency = "VND",
            PaymentMethod = PaymentSupport.PayOsProvider,
            PaymentPurpose = PaymentSupport.FullPurpose,
            PaymentStatus = PaymentSupport.PaidStatus,
            PaidAt = Now.AddDays(-3)
        };

        context.AddRange(route, booking, trip, ticket, payment);
        await context.SaveChangesAsync();
        return (trip, booking, ticket, payment);
    }
}
