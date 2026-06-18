using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Seats;

public class SeatLayoutFlowIntegrationTests
{
    [Test]
    public async Task FullStandardLayoutCreatesOnlyStandardSeatsWithoutService()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.AddRange(standard, vessel);
        await context.SaveChangesAsync();

        var plan = await SeatLayoutPlanner.BuildAsync(
            context,
            vessel,
            [new DeckConfigDto(1, 2, 2)],
            rejectExistingLayout: true,
            CancellationToken.None);

        plan.Seats.Count.ShouldBe(4);
        plan.Seats.ShouldAllBe(x => x.SeatType!.Code == "STANDARD");
    }

    [Test]
    public async Task FullStandardLayoutRejectsNonStandardCell()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var river = SeatFlowTestData.SeatType("RIVER");
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.AddRange(standard, river, vessel);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ValidationException>(() =>
            SeatLayoutPlanner.BuildAsync(
                context,
                vessel,
                [
                    new DeckConfigDto(
                        1,
                        2,
                        2,
                        Cells:
                        [
                            new LayoutCellConfigDto(
                                1,
                                1,
                                SeatLayoutCellType.Seat,
                                "RIVER")
                        ])
                ],
                rejectExistingLayout: true,
                CancellationToken.None));
    }

    [Test]
    public async Task StandardAndVipLayoutCreatesMixedSeededSeatTypesWithoutServicePrices()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var cabin = SeatFlowTestData.SeatType("CABIN");
        var river = SeatFlowTestData.SeatType("RIVER");
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.StandardAndVip);
        context.AddRange(cabin, river, vessel);
        await context.SaveChangesAsync();

        var plan = await SeatLayoutPlanner.BuildAsync(
            context,
            vessel,
            [
                new DeckConfigDto(
                    1,
                    2,
                    2,
                    Cells:
                    [
                        new LayoutCellConfigDto(
                            1,
                            1,
                            SeatLayoutCellType.Seat,
                            "RIVER")
                    ])
            ],
            rejectExistingLayout: true,
            CancellationToken.None);

        plan.Seats.Count(x => x.SeatType!.Code == "RIVER").ShouldBe(1);
        plan.Seats.Count(x => x.SeatType!.Code == "CABIN").ShouldBe(3);
    }

    [Test]
    public async Task CellsLayoutCanTreatSentSeatCellsAsExplicitLayout()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        vessel.SeatCount = 80;
        context.AddRange(standard, vessel);
        await context.SaveChangesAsync();

        var seatCells = Enumerable.Range(1, 80)
            .Select(index => new LayoutCellConfigDto(
                Row: ((index - 1) / 5) + 1,
                Column: ((index - 1) % 5) + 1,
                Type: SeatLayoutCellType.Seat))
            .ToArray();

        var plan = await SeatLayoutPlanner.BuildAsync(
            context,
            vessel,
            [new DeckConfigDto(1, 20, 6, Cells: seatCells)],
            rejectExistingLayout: true,
            CancellationToken.None);

        plan.Seats.Count.ShouldBe(80);
    }

    [Test]
    public async Task LayoutRejectsSeatTotalDifferentFromVesselSeatCount()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.AddRange(standard, vessel);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ValidationException>(() =>
            SeatLayoutPlanner.BuildAsync(
                context,
                vessel,
                [new DeckConfigDto(1, 1, 3)],
                rejectExistingLayout: true,
                CancellationToken.None));
    }
}
