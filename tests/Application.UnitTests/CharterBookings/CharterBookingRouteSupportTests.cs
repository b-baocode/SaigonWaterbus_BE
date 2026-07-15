using NUnit.Framework;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class CharterBookingRouteSupportTests
{
    [Test]
    public async Task DeletesOwnedRouteWhenCancelledBeforeTripCreation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = OwnedRoute("CB-20260714-ABC");
        var booking = CharterBooking("CB-20260714-ABC", BookingStatus.Cancelled, route);
        context.AddRange(route, booking);
        await context.SaveChangesAsync();

        await CharterBookingRouteSupport.DeactivateOwnedRouteAsync(context, booking, CancellationToken.None);
        await context.SaveChangesAsync();

        context.Set<Route>().Any(x => x.Id == route.Id).ShouldBeFalse();
        booking.CharterRouteId.ShouldBeNull();
    }

    [Test]
    public async Task InactivatesOwnedRouteWhenCancelledAfterTripCreation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = OwnedRoute("CB-20260714-ABC");
        var booking = CharterBooking("CB-20260714-ABC", BookingStatus.Cancelled, route);
        var trip = CharterTrip(route, booking);
        context.AddRange(route, booking, trip);
        await context.SaveChangesAsync();

        await CharterBookingRouteSupport.DeactivateOwnedRouteAsync(context, booking, CancellationToken.None);
        await context.SaveChangesAsync();

        var savedRoute = context.Set<Route>().Single(x => x.Id == route.Id);
        savedRoute.Status.ShouldBe("Inactive");
        booking.CharterRouteId.ShouldBe(route.Id);
    }

    [Test]
    public async Task InactivatesCompletedBookingRouteEvenWhenNoTripExists()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = OwnedRoute("CB-20260714-ABC");
        var booking = CharterBooking("CB-20260714-ABC", BookingStatus.Completed, route);
        context.AddRange(route, booking);
        await context.SaveChangesAsync();

        await CharterBookingRouteSupport.DeactivateOwnedRouteAsync(context, booking, CancellationToken.None);
        await context.SaveChangesAsync();

        var savedRoute = context.Set<Route>().Single(x => x.Id == route.Id);
        savedRoute.Status.ShouldBe("Inactive");
        booking.CharterRouteId.ShouldBe(route.Id);
    }

    private static Route OwnedRoute(string bookingCode) =>
        new()
        {
            RouteCode = CharterBookingRouteSupport.BuildCompactRouteCodeBase(bookingCode),
            RouteName = $"Charter {bookingCode}: Bến A - Bến B",
            RouteType = RouteTypes.Charter,
            Description = $"Route charter ghép từ booking {bookingCode}.",
            Status = "Active",
            IsBookable = false
        };

    private static Booking CharterBooking(string bookingCode, BookingStatus status, Route route) =>
        new()
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = bookingCode,
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = status,
            PaymentStatus = "Unpaid",
            CharterRouteId = route.Id,
            CharterRoute = route
        };

    private static Trip CharterTrip(Route route, Booking booking) =>
        new()
        {
            RouteId = route.Id,
            Route = route,
            TripCode = $"TRIP-{booking.BookingCode}",
            TripType = TripTypes.Charter,
            SourceBookingId = booking.Id,
            OperatingDate = new DateOnly(2026, 7, 14),
            DepartureTime = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero),
            ArrivalTime = new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero),
            CapacitySnapshot = 10,
            TripStatus = TripStatus.Cancelled
        };
}
