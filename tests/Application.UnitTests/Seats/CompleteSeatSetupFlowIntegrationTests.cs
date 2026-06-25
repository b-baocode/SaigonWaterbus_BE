using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Seats;

public class CompleteSeatSetupFlowIntegrationTests
{
    [Test]
    public async Task ConfigureEightySeatsFromElevenByEightMatrixWithOneAisleRow()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        vessel.SeatCount = 80;
        context.Add(vessel);
        await context.SaveChangesAsync();

        await GenerateMatrix(context, userContext, vessel.Id, 11, 8);

        var cells = new List<LayoutCellConfigDto>();
        for (var row = 1; row <= 11; row++)
        {
            for (var column = 1; column <= 8; column++)
            {
                cells.Add(new LayoutCellConfigDto(
                    row,
                    column,
                    row == 10 ? SeatLayoutCellType.Aisle : SeatLayoutCellType.Seat));
            }
        }

        var configured = await Configure(
            context,
            userContext,
            vessel.Id,
            [new DeckConfigDto(1, 11, 8, Cells: cells)]);

        configured.ConfiguredSeats.ShouldBe(80);
        configured.SeatsConfigured.ShouldBeTrue();
        configured.Decks.Single().Cells.Count.ShouldBe(88);
        configured.Decks.Single().Cells.Count(x => x.Type == SeatLayoutCellType.Aisle).ShouldBe(8);
        vessel.Status.ShouldBe(VesselStatus.Active);
        (await context.Seats.CountAsync(x => x.VesselId == vessel.Id)).ShouldBe(80);

        var fetched = await new GetSeatsRequestUseCase(context, userContext)
            .ExecuteAsync(new GetSeatsRequest(vessel.Id), CancellationToken.None);

        fetched.Decks.Single().Cells.Count.ShouldBe(88);
        fetched.Decks.Single().Cells
            .Where(x => x.Row == 10)
            .ShouldAllBe(x => x.Type == SeatLayoutCellType.Aisle);
        fetched.Decks.Single().Cells
            .Where(x => x.Type == SeatLayoutCellType.Seat)
            .Count()
            .ShouldBe(80);
    }

    [Test]
    public async Task ConfigureTreatsCellsAsOverridesAndDefaultsOtherCellsToSeats()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        vessel.SeatCount = 80;
        context.Add(vessel);
        await context.SaveChangesAsync();

        var cells = Enumerable.Range(1, 14)
            .Select(row => new LayoutCellConfigDto(row, 4, SeatLayoutCellType.Aisle))
            .Concat(
            [
                new LayoutCellConfigDto(14, 1, SeatLayoutCellType.Empty),
                new LayoutCellConfigDto(14, 2, SeatLayoutCellType.Empty),
                new LayoutCellConfigDto(14, 3, SeatLayoutCellType.Empty),
                new LayoutCellConfigDto(14, 5, SeatLayoutCellType.Empty)
            ])
            .ToArray();

        var configured = await Configure(
            context,
            userContext,
            vessel.Id,
            [new DeckConfigDto(1, 14, 7, Cells: cells)]);

        configured.ConfiguredSeats.ShouldBe(80);
        configured.Decks.Single().Cells.Count(x => x.Type == SeatLayoutCellType.Seat).ShouldBe(80);
        (await context.Seats.CountAsync(x => x.VesselId == vessel.Id)).ShouldBe(80);
    }

    [Test]
    public async Task FullStandardFlowGeneratesMatrixConfiguresSeatsAndActivatesVessel()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.Add(vessel);
        await context.SaveChangesAsync();

        var matrix = await GenerateMatrix(context, userContext, vessel.Id, 2, 2);

        matrix.ConfiguredSeats.ShouldBe(0);
        matrix.Decks.Single().RowCount.ShouldBe(2);
        matrix.Decks.Single().Cells.Count.ShouldBe(4);
        matrix.Decks.Single().Cells.ShouldAllBe(x => x.Type == SeatLayoutCellType.Seat);
        matrix.Decks.Single().Cells.ShouldAllBe(x =>
            x.SeatType != null && x.SeatType.SeatTypeCode == "STANDARD");
        (await context.Seats.CountAsync(x => x.VesselId == vessel.Id)).ShouldBe(0);
        vessel.Status.ShouldBe(VesselStatus.Inactive);

        var configured = await Configure(
            context,
            userContext,
            vessel.Id,
            [new DeckConfigDto(1, 2, 2)]);

        configured.ConfiguredSeats.ShouldBe(4);
        configured.SeatsConfigured.ShouldBeTrue();
        vessel.Status.ShouldBe(VesselStatus.Active);
        (await context.Seats
                .Where(x => x.VesselId == vessel.Id)
                .ToListAsync())
            .ShouldAllBe(x => x.SeatTypeName == "Standard");
    }

    [Test]
    public async Task SightseeingFlowPersistsMixedSeatTypesAndActivatesVessel()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.StandardAndVip);
        vessel.SeatCount = 3;
        context.Add(vessel);
        await context.SaveChangesAsync();

        var matrix = await GenerateMatrix(context, userContext, vessel.Id, 2, 2);
        matrix.Decks.Single().Cells.ShouldAllBe(x =>
            x.SeatType != null && x.SeatType.SeatTypeCode == "CABIN");

        var configured = await Configure(
            context,
            userContext,
            vessel.Id,
            [
                new DeckConfigDto(
                    1,
                    2,
                    2,
                    Cells:
                    [
                        new LayoutCellConfigDto(1, 1, SeatLayoutCellType.Seat, "RIVER"),
                        new LayoutCellConfigDto(1, 2, SeatLayoutCellType.Seat, "SKY"),
                        new LayoutCellConfigDto(2, 1, SeatLayoutCellType.Seat, "CABIN"),
                        new LayoutCellConfigDto(2, 2, SeatLayoutCellType.Aisle)
                    ])
            ]);

        configured.ConfiguredSeats.ShouldBe(3);
        configured.SeatsConfigured.ShouldBeTrue();
        vessel.Status.ShouldBe(VesselStatus.Active);

        var seats = await context.Seats
            .Where(x => x.VesselId == vessel.Id)
            .ToListAsync();
        seats.Count(x => x.SeatTypeName == "River").ShouldBe(1);
        seats.Count(x => x.SeatTypeName == "Sky").ShouldBe(1);
        seats.Count(x => x.SeatTypeName == "Cabin").ShouldBe(1);
    }

    [Test]
    public async Task GetSeatsReturnsConfiguredSeatTypesForSightseeingVessel()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.StandardAndVip);
        vessel.SeatCount = 6;
        context.Add(vessel);
        await context.SaveChangesAsync();

        await GenerateMatrix(context, userContext, vessel.Id, 3, 4);

        await Configure(
            context,
            userContext,
            vessel.Id,
            [
                new DeckConfigDto(
                    1,
                    3,
                    4,
                    Cells:
                    [
                        new LayoutCellConfigDto(1, 1, SeatLayoutCellType.Seat, "RIVER"),
                        new LayoutCellConfigDto(1, 2, SeatLayoutCellType.Seat, "SKY"),
                        new LayoutCellConfigDto(1, 3, SeatLayoutCellType.Aisle),
                        new LayoutCellConfigDto(1, 4, SeatLayoutCellType.Seat, "CABIN"),
                        new LayoutCellConfigDto(2, 1, SeatLayoutCellType.Seat, "CABIN"),
                        new LayoutCellConfigDto(2, 2, SeatLayoutCellType.Empty),
                        new LayoutCellConfigDto(2, 3, SeatLayoutCellType.Empty),
                        new LayoutCellConfigDto(2, 4, SeatLayoutCellType.Empty),
                        new LayoutCellConfigDto(3, 1, SeatLayoutCellType.Seat, "CABIN"),
                        new LayoutCellConfigDto(3, 2, SeatLayoutCellType.Seat, "CABIN"),
                        new LayoutCellConfigDto(3, 3, SeatLayoutCellType.Aisle),
                        new LayoutCellConfigDto(3, 4, SeatLayoutCellType.Empty)
                    ])
            ]);

        var fetched = await new GetSeatsRequestUseCase(context, userContext)
            .ExecuteAsync(new GetSeatsRequest(vessel.Id), CancellationToken.None);

        var cells = fetched.Decks.Single().Cells;
        cells.Count.ShouldBe(12);
        cells.Count(x => x.Type == SeatLayoutCellType.Seat).ShouldBe(6);
        cells.Count(x => x.Seat?.SeatType?.SeatTypeCode == "RIVER").ShouldBe(1);
        cells.Count(x => x.Seat?.SeatType?.SeatTypeCode == "SKY").ShouldBe(1);
        cells.Count(x => x.Seat?.SeatType?.SeatTypeCode == "CABIN").ShouldBe(4);
        cells.Where(x => x.Type != SeatLayoutCellType.Seat)
            .ShouldAllBe(x => x.Type == SeatLayoutCellType.Aisle);
    }

    [Test]
    public async Task ConfigureWithoutGeneratingMatrixStillSucceeds()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.Add(vessel);
        await context.SaveChangesAsync();

        var configured = await Configure(
            context,
            userContext,
            vessel.Id,
            [new DeckConfigDto(1, 2, 2)]);

        configured.ConfiguredSeats.ShouldBe(4);
        configured.SeatsConfigured.ShouldBeTrue();
        vessel.SeatsConfigured.ShouldBeTrue();
        vessel.Status.ShouldBe(VesselStatus.Active);
        (await context.Seats.CountAsync(x => x.VesselId == vessel.Id)).ShouldBe(4);
    }

    [Test]
    public async Task ConfigureRejectsSeatCountDifferentFromVessel()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.Add(vessel);
        await context.SaveChangesAsync();
        await GenerateMatrix(context, userContext, vessel.Id, 2, 2);

        await Should.ThrowAsync<ValidationException>(() =>
            Configure(
                context,
                userContext,
                vessel.Id,
                [new DeckConfigDto(1, 1, 2)]));

        context.Seats.ShouldBeEmpty();
        vessel.SeatsConfigured.ShouldBeFalse();
    }

    [Test]
    public async Task GenerateMatrixRejectsDeckCountDifferentFromVessel()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        vessel.NumberOfDecks = 2;
        context.Add(vessel);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ValidationException>(() =>
            GenerateMatrix(context, userContext, vessel.Id, 2, 2));

        context.Seats.ShouldBeEmpty();
    }

    [Test]
    public async Task GenerateMatrixRejectsVesselThatAlreadyHasSeats()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.Add(vessel);
        await context.SaveChangesAsync();
        await GenerateMatrix(context, userContext, vessel.Id, 2, 2);
        await Configure(context, userContext, vessel.Id, [new DeckConfigDto(1, 2, 2)]);

        await Should.ThrowAsync<ValidationException>(() =>
            GenerateMatrix(context, userContext, vessel.Id, 2, 2));

        (await context.Seats.CountAsync(x => x.VesselId == vessel.Id)).ShouldBe(4);
    }

    [Test]
    public async Task ConfigureDoesNotRequireService()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.Add(vessel);
        await context.SaveChangesAsync();
        await GenerateMatrix(context, userContext, vessel.Id, 2, 2);

        var configured = await Configure(
            context,
            userContext,
            vessel.Id,
            [new DeckConfigDto(1, 2, 2)]);

        configured.SeatsConfigured.ShouldBeTrue();
        vessel.Status.ShouldBe(VesselStatus.Active);
    }

    [Test]
    public async Task ManagerCannotGenerateSeatMatrix()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedManagerAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.Add(vessel);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            GenerateMatrix(context, userContext, vessel.Id, 2, 2));

        context.Seats.ShouldBeEmpty();
    }

    private static Task<VesselSeatsDto> GenerateMatrix(
        Infrastructure.Data.ApplicationDbContext context,
        TestUserContext userContext,
        Guid vesselId,
        int rows,
        int columns) =>
        new GenerateSeatMatrixRequestUseCase(context, userContext).ExecuteAsync(
            new GenerateSeatMatrixRequest(
                vesselId,
                [new DeckMatrixConfigDto(1, rows, columns)]),
            CancellationToken.None);

    private static Task<VesselSeatsDto> Configure(
        Infrastructure.Data.ApplicationDbContext context,
        TestUserContext userContext,
        Guid vesselId,
        IReadOnlyCollection<DeckConfigDto> decks) =>
        new GenerateSeatsRequestUseCase(context, userContext).ExecuteAsync(
            new GenerateSeatsRequest(vesselId, decks),
            CancellationToken.None);
}
