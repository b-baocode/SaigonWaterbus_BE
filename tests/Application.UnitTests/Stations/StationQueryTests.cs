using NUnit.Framework;
using SaigonWaterbus.Application.Stations;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Stations;

public class StationQueryTests
{
    [Test]
    public async Task GetStationListReturnsActiveAndInactiveStations()
    {
        await using var context = SeatFlowTestData.CreateContext();
        context.Stations.AddRange(
            Station("BD", "Bach Dang", StationStatus.Active),
            Station("TD", "Thu Duc", StationStatus.Inactive));
        await context.SaveChangesAsync();

        var result = await new GetStationListQueryHandler(context)
            .Handle(new GetStationListQuery(), CancellationToken.None);

        result.Count.ShouldBe(2);
        result.Select(x => x.Status).ShouldBe([nameof(StationStatus.Active), nameof(StationStatus.Inactive)]);
    }

    [Test]
    public async Task UpdateStationStatusChangesStatusOnly()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var station = Station("BD", "Bach Dang", StationStatus.Active);
        station.IsWaterbusStation = false;
        context.Stations.Add(station);
        await context.SaveChangesAsync();

        var result = await new UpdateStationStatusCommandHandler(context)
            .Handle(new UpdateStationStatusCommand(station.Id, StationStatus.Inactive), CancellationToken.None);

        result.Status.ShouldBe(nameof(StationStatus.Inactive));
        result.IsWaterbusStation.ShouldBeFalse();
        var updatedStation = await context.Stations.FindAsync(station.Id);
        updatedStation!.Status.ShouldBe(StationStatus.Inactive);
    }

    private static Station Station(string code, string name, StationStatus status) =>
        new()
        {
            StationCode = code,
            StationName = name,
            Status = status
        };
}
