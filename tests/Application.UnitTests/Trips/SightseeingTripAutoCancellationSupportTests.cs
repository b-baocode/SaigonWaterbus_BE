using NUnit.Framework;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Trips;

public class SightseeingTripAutoCancellationSupportTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task CancelsSightseeingTripWithinFiveMinutesWhenNoConfirmedPassengers()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var trip = await SeedTripAsync(context, "SIG-AUTO-1", RouteTypes.SightseeingLoop, Now.AddMinutes(5));

        var cancelled = await SightseeingTripAutoCancellationSupport.CancelDueEmptySightseeingTripsAsync(
            context,
            Now,
            CancellationToken.None);

        cancelled.ShouldBe(1);
        trip.TripStatus.ShouldBe(TripStatus.Cancelled);
        trip.StatusNote.ShouldNotBeNull();
        trip.StatusNote.ShouldContain("không có khách");
    }

    [Test]
    public async Task DoesNotCancelSightseeingTripWhenAConfirmedPassengerExists()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var trip = await SeedTripAsync(context, "SIG-AUTO-2", RouteTypes.SightseeingLoop, Now.AddMinutes(5));
        await SeedBookingAsync(context, trip, BookingStatus.Confirmed);

        var cancelled = await SightseeingTripAutoCancellationSupport.CancelDueEmptySightseeingTripsAsync(
            context,
            Now,
            CancellationToken.None);

        cancelled.ShouldBe(0);
        trip.TripStatus.ShouldBe(TripStatus.Scheduled);
    }

    [Test]
    public async Task PendingPaymentDoesNotKeepSightseeingTripAlive()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var trip = await SeedTripAsync(context, "SIG-AUTO-3", RouteTypes.SightseeingLoop, Now.AddMinutes(5));
        var booking = await SeedBookingAsync(context, trip, BookingStatus.PendingPayment);
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "9000001",
            Provider = PaymentSupport.PayOsProvider,
            Amount = 150000m,
            Currency = "VND",
            PaymentMethod = PaymentSupport.PayOsProvider,
            PaymentPurpose = PaymentSupport.FullPurpose,
            PaymentStatus = PaymentSupport.PendingStatus
        };
        context.Add(payment);
        await context.SaveChangesAsync();

        var cancelled = await SightseeingTripAutoCancellationSupport.CancelDueEmptySightseeingTripsAsync(
            context,
            Now,
            CancellationToken.None);

        cancelled.ShouldBe(1);
        trip.TripStatus.ShouldBe(TripStatus.Cancelled);
        booking.BookingStatus.ShouldBe(BookingStatus.Expired);
        payment.PaymentStatus.ShouldBe(PaymentSupport.ExpiredStatus);
    }

    [Test]
    public async Task IgnoresRegularTripsAndSightseeingTripsOutsideFiveMinuteWindow()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var regular = await SeedTripAsync(context, "REG-AUTO-1", RouteTypes.Regular, Now.AddMinutes(5));
        var laterSightseeing = await SeedTripAsync(context, "SIG-AUTO-4", RouteTypes.SightseeingLoop, Now.AddMinutes(6));

        var cancelled = await SightseeingTripAutoCancellationSupport.CancelDueEmptySightseeingTripsAsync(
            context,
            Now,
            CancellationToken.None);

        cancelled.ShouldBe(0);
        regular.TripStatus.ShouldBe(TripStatus.Scheduled);
        laterSightseeing.TripStatus.ShouldBe(TripStatus.Scheduled);
    }

    private static async Task<Trip> SeedTripAsync(
        Infrastructure.Data.ApplicationDbContext context,
        string tripCode,
        string routeType,
        DateTimeOffset departureTime)
    {
        var route = new Route
        {
            RouteCode = $"R-{tripCode}",
            RouteName = $"Route {tripCode}",
            RouteType = routeType,
            IsBookable = true
        };
        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            TripCode = tripCode,
            TripType = TripTypes.Regular,
            OperatingDate = DateOnly.FromDateTime(departureTime.UtcDateTime),
            DepartureTime = departureTime,
            ArrivalTime = departureTime.AddHours(1),
            CapacitySnapshot = 10,
            TripStatus = TripStatus.Scheduled
        };

        context.AddRange(route, trip);
        await context.SaveChangesAsync();
        return trip;
    }

    private static async Task<Booking> SeedBookingAsync(
        Infrastructure.Data.ApplicationDbContext context,
        Trip trip,
        BookingStatus status)
    {
        var booking = new Booking
        {
            BookingType = Booking.SeatBookingType,
            Trip = trip,
            TripId = trip.Id,
            BookingCode = $"BK-{trip.TripCode}",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = status,
            PaymentStatus = status == BookingStatus.Confirmed ? PaymentSupport.PaidBookingPaymentStatus : "Unpaid",
            SubtotalAmount = 150000m,
            TotalAmount = 150000m,
            RemainingAmount = status == BookingStatus.Confirmed ? 0m : 150000m
        };
        booking.Passengers.Add(new BookingPassenger
        {
            Booking = booking,
            Trip = trip,
            TripId = trip.Id,
            FullName = "Nguyen Van A",
            PassengerType = "ADULT",
            UnitPrice = 150000m
        });

        context.Add(booking);
        await context.SaveChangesAsync();
        return booking;
    }
}
