using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Stations;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.UnitTests.Stations;

public class StationDeleteTests
{
    [Test]
    public async Task DeleteUnusedStationRemovesStation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var station = Station("BD", "Bach Dang");
        context.Stations.Add(station);
        await context.SaveChangesAsync();

        await new DeleteStationCommandHandler(context)
            .Handle(new DeleteStationCommand(station.Id), CancellationToken.None);

        (await context.Stations.AnyAsync(s => s.Id == station.Id)).ShouldBeFalse();
    }

    [Test]
    public async Task DeleteStationUsedByRouteReturnsValidation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var station = Station("BD", "Bach Dang");
        var route = new Route
        {
            RouteCode = "R1",
            RouteName = "Route 1"
        };
        context.AddRange(
            station,
            route,
            new RouteStop
            {
                Route = route,
                Station = station,
                StopOrder = 1
            });
        await context.SaveChangesAsync();

        var act = async () => await new DeleteStationCommandHandler(context)
            .Handle(new DeleteStationCommand(station.Id), CancellationToken.None);

        var exception = await act.ShouldThrowAsync<ValidationException>();
        exception.Errors["stationId"].Single().ShouldContain("route");
        (await context.Stations.AnyAsync(s => s.Id == station.Id)).ShouldBeTrue();
    }

    [Test]
    public async Task DeleteStationUsedByBookingReturnsValidation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var station = Station("NVL", "Nguyen Van Linh");
        context.AddRange(
            station,
            new Booking
            {
                BookingType = Booking.CharterBookingType,
                BookingCode = "BK-DELETE-STATION",
                ContactName = "Nguyen Van A",
                ContactPhone = "0900000000",
                BookingStatus = BookingStatus.PendingQuote,
                PaymentStatus = "Unpaid",
                Currency = "VND",
                FromStation = station
            });
        await context.SaveChangesAsync();

        var act = async () => await new DeleteStationCommandHandler(context)
            .Handle(new DeleteStationCommand(station.Id), CancellationToken.None);

        var exception = await act.ShouldThrowAsync<ValidationException>();
        exception.Errors["stationId"].Single().ShouldContain("booking");
        (await context.Stations.AnyAsync(s => s.Id == station.Id)).ShouldBeTrue();
    }

    private static Station Station(string code, string name) =>
        new()
        {
            StationCode = code,
            StationName = name,
            Status = StationStatus.Active
        };
}
