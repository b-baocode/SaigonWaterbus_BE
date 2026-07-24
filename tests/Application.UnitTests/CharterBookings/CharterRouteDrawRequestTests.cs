using NetTopologySuite.Geometries;
using NUnit.Framework;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class CharterRouteDrawRequestTests
{
    [Test]
    public async Task AdminCanCreateDrawRequestWithStopsAndCandidateRoute()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var (booking, route) = await SeedBookingWithCandidateRouteAsync(context);

        var result = await new CreateCharterRouteDrawRequestCommandHandler(context, adminContext)
            .Handle(new CreateCharterRouteDrawRequestCommand(booking.Id, "Can ve tu GPS"), CancellationToken.None);

        result.BookingId.ShouldBe(booking.Id);
        result.Status.ShouldBe(CharterRouteDrawRequest.PendingStatus);
        result.Stops.Select(x => x.StationId).ShouldBe([
            booking.FromStationId!.Value,
            booking.ItineraryStops.Single().StationId,
            booking.ToStationId!.Value
        ]);
        result.CandidateRoute!.RouteId.ShouldBe(route.Id);
        result.CandidateRoute.RouteGeometry!.Count.ShouldBeGreaterThan(1);

        var duplicate = await new CreateCharterRouteDrawRequestCommandHandler(context, adminContext)
            .Handle(new CreateCharterRouteDrawRequestCommand(booking.Id), CancellationToken.None);

        duplicate.RequestId.ShouldBe(result.RequestId);
        context.Set<CharterRouteDrawRequest>().Count().ShouldBe(1);
    }

    [Test]
    public async Task CompleteDrawRequestLinksRouteToBookingAndIsIdempotent()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var now = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var (booking, route) = await SeedBookingWithCandidateRouteAsync(context);
        var request = await new CreateCharterRouteDrawRequestCommandHandler(context, adminContext)
            .Handle(new CreateCharterRouteDrawRequestCommand(booking.Id), CancellationToken.None);

        var handler = new CompleteCharterRouteDrawRequestCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(now));

        var result = await handler.Handle(
            new CompleteCharterRouteDrawRequestCommand(request.RequestId, route.Id),
            CancellationToken.None);
        var second = await handler.Handle(
            new CompleteCharterRouteDrawRequestCommand(request.RequestId, route.Id),
            CancellationToken.None);

        result.Status.ShouldBe(CharterRouteDrawRequest.DoneStatus);
        result.ResultRoute!.RouteId.ShouldBe(route.Id);
        result.CompletedAt.ShouldBe(now);
        second.ResultRoute!.RouteId.ShouldBe(route.Id);

        var savedBooking = context.Set<Booking>().Single(x => x.Id == booking.Id);
        savedBooking.CharterRouteId.ShouldBe(route.Id);
    }

    private static async Task<(Booking Booking, Route Route)> SeedBookingWithCandidateRouteAsync(
        Infrastructure.Data.ApplicationDbContext context)
    {
        var from = Station("BD", "Bach Dang", 10.0m, 106.0m);
        var middle = Station("TT", "Thanh Da", 10.1m, 106.1m);
        var to = Station("LD", "Linh Dong", 10.2m, 106.2m);
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = $"CB-{Guid.NewGuid():N}"[..20],
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.PendingQuote,
            PaymentStatus = "Unpaid",
            Currency = "VND",
            DepartureDate = new DateOnly(2030, 1, 2),
            StartTime = new TimeOnly(8, 0),
            PassengerCount = 12,
            AdultCount = 10,
            ChildCount = 2,
            FromStation = from,
            FromStationId = from.Id,
            ToStation = to,
            ToStationId = to.Id
        };
        booking.ItineraryStops.Add(new BookingItineraryStop
        {
            Booking = booking,
            BookingId = booking.Id,
            Station = middle,
            StationId = middle.Id,
            StopOrder = 1,
            StayDurationMinutes = 20,
            Note = "Dung chup anh"
        });

        var route = new Route
        {
            RouteCode = $"CR-{Guid.NewGuid():N}"[..20],
            RouteName = "Charter reference route",
            RouteType = RouteTypes.CharterReference,
            Status = "Active",
            IsBookable = false,
            RouteGeometry = new LineString([
                new Coordinate(106.0, 10.0),
                new Coordinate(106.1, 10.1),
                new Coordinate(106.2, 10.2)
            ])
            { SRID = 4326 }
        };
        route.RouteStops =
        [
            RouteStop(route, from, 1),
            RouteStop(route, middle, 2),
            RouteStop(route, to, 3)
        ];

        context.AddRange(from, middle, to, booking, route);
        await context.SaveChangesAsync();
        return (booking, route);
    }

    private static Station Station(string code, string name, decimal latitude, decimal longitude) =>
        new()
        {
            StationCode = code,
            StationName = name,
            Latitude = latitude,
            Longitude = longitude,
            IsWaterbusStation = true,
            Status = StationStatus.Active
        };

    private static RouteStop RouteStop(Route route, Station station, int order) =>
        new()
        {
            Route = route,
            RouteId = route.Id,
            Station = station,
            StationId = station.Id,
            StopOrder = order,
            IsPickupAllowed = order < 3,
            IsDropoffAllowed = order > 1
        };
}
