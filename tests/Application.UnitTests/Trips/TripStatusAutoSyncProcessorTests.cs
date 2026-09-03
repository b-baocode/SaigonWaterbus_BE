using NUnit.Framework;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Trips;

public class TripStatusAutoSyncProcessorTests
{
    [Test]
    public async Task CompletedTripAlsoCompletesConfirmedSourceBooking()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var now = new DateTimeOffset(2030, 1, 1, 3, 0, 0, TimeSpan.Zero);
        var route = Route();
        var booking = Booking();
        var trip = Trip(route, booking, now.AddHours(-2), now.AddHours(-1));
        booking.TripId = trip.Id;
        booking.Trip = trip;
        context.AddRange(route, booking, trip);
        await context.SaveChangesAsync();

        var result = await new TripStatusAutoSyncProcessor(context)
            .SyncAsync(now, CancellationToken.None);

        result.ArrivedTripCount.ShouldBe(1);
        result.CompletedBookingCount.ShouldBe(1);
        trip.TripStatus.ShouldBe(TripStatus.Completed);
        booking.BookingStatus.ShouldBe(BookingStatus.Completed);
        booking.CompletedAt.ShouldBe(now);
        booking.CompletionSource.ShouldBe($"TripCompleted:{trip.TripCode}");
    }

    [Test]
    public async Task DepartedTripDoesNotCompleteSourceBooking()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var now = new DateTimeOffset(2030, 1, 1, 3, 0, 0, TimeSpan.Zero);
        var route = Route();
        var booking = Booking();
        var trip = Trip(route, booking, now.AddMinutes(-10), now.AddMinutes(50));
        booking.TripId = trip.Id;
        booking.Trip = trip;
        context.AddRange(route, booking, trip);
        await context.SaveChangesAsync();

        var result = await new TripStatusAutoSyncProcessor(context)
            .SyncAsync(now, CancellationToken.None);

        result.DepartedTripCount.ShouldBe(1);
        result.CompletedBookingCount.ShouldBe(0);
        trip.TripStatus.ShouldBe(TripStatus.InProgress);
        booking.BookingStatus.ShouldBe(BookingStatus.Confirmed);
    }

    [Test]
    public async Task ExistingCompletedTripRepairsConfirmedSourceBooking()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var now = new DateTimeOffset(2030, 1, 1, 3, 0, 0, TimeSpan.Zero);
        var route = Route();
        var booking = Booking();
        var trip = Trip(route, booking, now.AddHours(-2), now.AddHours(-1));
        trip.TripStatus = TripStatus.Completed;
        booking.TripId = trip.Id;
        booking.Trip = trip;
        context.AddRange(route, booking, trip);
        await context.SaveChangesAsync();

        var result = await new TripStatusAutoSyncProcessor(context)
            .SyncAsync(now, CancellationToken.None);

        result.ArrivedTripCount.ShouldBe(0);
        result.CompletedBookingCount.ShouldBe(1);
        booking.BookingStatus.ShouldBe(BookingStatus.Completed);
        booking.CompletedAt.ShouldBe(now);
    }

    private static Booking Booking() => new()
    {
        BookingType = SaigonWaterbus.Domain.Entities.Booking.CharterBookingType,
        BookingCode = $"CB-{Guid.NewGuid():N}"[..20],
        ContactName = "Nguyen Van A",
        ContactPhone = "0900000000",
        BookingStatus = BookingStatus.Confirmed,
        PaymentStatus = "Paid",
        DepartureDate = new DateOnly(2030, 1, 1),
        StartTime = new TimeOnly(8, 0),
        RentalUnit = BoatRentalUnit.Hour,
        DurationValue = 1,
        PassengerCount = 1
    };

    private static Trip Trip(
        Route route,
        Booking booking,
        DateTimeOffset departure,
        DateTimeOffset arrival) => new()
    {
        RouteId = route.Id,
        Route = route,
        TripCode = $"TRIP-{Guid.NewGuid():N}"[..20],
        TripType = TripTypes.Charter,
        SourceBookingId = booking.Id,
        OperatingDate = DateOnly.FromDateTime(departure.UtcDateTime),
        DepartureTime = departure,
        ArrivalTime = arrival,
        CapacitySnapshot = 10,
        TripStatus = TripStatus.Scheduled
    };

    private static Route Route() => new()
    {
        RouteCode = $"ROUTE-{Guid.NewGuid():N}"[..20],
        RouteName = "Charter route",
        RouteType = RouteTypes.Charter,
        Status = "Active"
    };
}
