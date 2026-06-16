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
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        vessel.SeatCount = 80;
        context.AddRange(standard, vessel);
        await context.SaveChangesAsync();

        await GenerateMatrix(context, userContext, vessel.Id, 11, 8);
        var aisleCells = Enumerable.Range(1, 8)
            .Select(column => new LayoutCellConfigDto(
                10,
                column,
                SeatLayoutCellType.Aisle))
            .ToArray();

        var configured = await Configure(
            context,
            userContext,
            vessel.Id,
            [new DeckConfigDto(1, 11, 8, Cells: aisleCells)]);

        configured.ConfiguredSeats.ShouldBe(80);
        configured.SeatsConfigured.ShouldBeTrue();
        configured.Decks.Single().Cells.Count.ShouldBe(88);
        configured.Decks.Single().Cells.Count(x => x.Type == SeatLayoutCellType.Aisle).ShouldBe(8);
        vessel.Status.ShouldBe(VesselStatus.Active);
        (await context.Seats.CountAsync(x => x.VesselId == vessel.Id)).ShouldBe(80);
        (await context.VesselLayoutCells.CountAsync(x => x.VesselId == vessel.Id)).ShouldBe(8);

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
    public async Task FullStandardFlowGeneratesMatrixConfiguresSeatsAndActivatesVessel()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.AddRange(standard, vessel);
        await context.SaveChangesAsync();

        var matrix = await GenerateMatrix(context, userContext, vessel.Id, 2, 2);

        matrix.ConfiguredSeats.ShouldBe(0);
        matrix.Decks.Single().RowCount.ShouldBe(2);
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
                .Include(x => x.SeatType)
                .Where(x => x.VesselId == vessel.Id)
                .ToListAsync())
            .ShouldAllBe(x => x.SeatType!.Code == "STANDARD");
    }

    [Test]
    public async Task StandardAndVipFlowPersistsMixedSeatsAndActivatesVessel()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vip = SeatFlowTestData.SeatType("VIP");
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.StandardAndVip);
        vessel.SeatCount = 3;
        context.AddRange(standard, vip, vessel);
        await context.SaveChangesAsync();

        await GenerateMatrix(context, userContext, vessel.Id, 2, 2);
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
                        new LayoutCellConfigDto(
                            1,
                            1,
                            SeatLayoutCellType.Seat,
                            "VIP"),
                        new LayoutCellConfigDto(
                            1,
                            2,
                            SeatLayoutCellType.Aisle)
                    ])
            ]);

        configured.ConfiguredSeats.ShouldBe(3);
        configured.SeatsConfigured.ShouldBeTrue();
        vessel.Status.ShouldBe(VesselStatus.Active);

        var seats = await context.Seats
            .Include(x => x.SeatType)
            .Where(x => x.VesselId == vessel.Id)
            .ToListAsync();
        seats.Count(x => x.SeatType!.Code == "VIP").ShouldBe(1);
        seats.Count(x => x.SeatType!.Code == "STANDARD").ShouldBe(2);
    }

    [Test]
    public async Task GetSeatsReturnsMixedLayoutCellsWithSeatsAislesEmptyCellsAndToilet()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vip = SeatFlowTestData.SeatType("VIP");
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.StandardAndVip);
        vessel.SeatCount = 6;
        context.AddRange(standard, vip, vessel);
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
                        new LayoutCellConfigDto(1, 1, SeatLayoutCellType.Seat, "VIP"),
                        new LayoutCellConfigDto(1, 2, SeatLayoutCellType.Seat, "VIP"),
                        new LayoutCellConfigDto(1, 3, SeatLayoutCellType.Aisle),
                        new LayoutCellConfigDto(1, 4, SeatLayoutCellType.Seat, "STANDARD"),
                        new LayoutCellConfigDto(2, 1, SeatLayoutCellType.Seat, "STANDARD"),
                        new LayoutCellConfigDto(2, 2, SeatLayoutCellType.Toilet, RowSpan: 1, ColumnSpan: 2),
                        new LayoutCellConfigDto(2, 4, SeatLayoutCellType.Empty),
                        new LayoutCellConfigDto(3, 1, SeatLayoutCellType.Seat, "STANDARD"),
                        new LayoutCellConfigDto(3, 2, SeatLayoutCellType.Seat, "STANDARD"),
                        new LayoutCellConfigDto(3, 3, SeatLayoutCellType.Aisle),
                        new LayoutCellConfigDto(3, 4, SeatLayoutCellType.Empty)
                    ])
            ]);

        var fetched = await new GetSeatsRequestUseCase(context, userContext)
            .ExecuteAsync(new GetSeatsRequest(vessel.Id), CancellationToken.None);

        var cells = fetched.Decks.Single().Cells;
        cells.Count.ShouldBe(12);
        cells.Count(x => x.Type == SeatLayoutCellType.Seat).ShouldBe(6);
        cells.Count(x => x.Type == SeatLayoutCellType.Aisle).ShouldBe(2);
        cells.Count(x => x.Type == SeatLayoutCellType.Empty).ShouldBe(2);
        cells.Count(x => x.Type == SeatLayoutCellType.Toilet).ShouldBe(2);
        cells.Count(x => x.Seat?.SeatType?.SeatTypeCode == "VIP").ShouldBe(2);
        cells.Count(x => x.Seat?.SeatType?.SeatTypeCode == "STANDARD").ShouldBe(4);
        cells.ShouldContain(x => x.Row == 2 && x.Column == 2 && x.Type == SeatLayoutCellType.Toilet && x.Facility != null);
        cells.ShouldContain(x => x.Row == 2 && x.Column == 3 && x.Type == SeatLayoutCellType.Toilet && x.Facility != null);
        cells.ShouldContain(x => x.Row == 1 && x.Column == 3 && x.Type == SeatLayoutCellType.Aisle);
        cells.ShouldContain(x => x.Row == 3 && x.Column == 3 && x.Type == SeatLayoutCellType.Aisle);
        cells.ShouldContain(x => x.Row == 2 && x.Column == 4 && x.Type == SeatLayoutCellType.Empty);
        cells.ShouldContain(x => x.Row == 3 && x.Column == 4 && x.Type == SeatLayoutCellType.Empty);
    }

    [Test]
    public async Task ConfigureBeforeGenerateMatrixIsRejected()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.AddRange(standard, vessel);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ValidationException>(() =>
            Configure(
                context,
                userContext,
                vessel.Id,
                [new DeckConfigDto(1, 2, 2)]));

        context.Seats.ShouldBeEmpty();
        vessel.SeatsConfigured.ShouldBeFalse();
        vessel.Status.ShouldBe(VesselStatus.Inactive);
    }

    [Test]
    public async Task ConfigureRejectsDimensionsDifferentFromGeneratedMatrix()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.AddRange(standard, vessel);
        await context.SaveChangesAsync();
        await GenerateMatrix(context, userContext, vessel.Id, 2, 2);

        await Should.ThrowAsync<ValidationException>(() =>
            Configure(
                context,
                userContext,
                vessel.Id,
                [new DeckConfigDto(1, 1, 4)]));

        context.Seats.ShouldBeEmpty();
        vessel.SeatsConfigured.ShouldBeFalse();
    }

    [Test]
    public async Task GenerateMatrixRejectsDeckCountDifferentFromVessel()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        vessel.NumberOfDecks = 2;
        context.AddRange(standard, vessel);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ValidationException>(() =>
            GenerateMatrix(context, userContext, vessel.Id, 2, 2));

        context.VesselDeckLayouts.ShouldBeEmpty();
    }

    [Test]
    public async Task GenerateMatrixRejectsExistingMatrix()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.AddRange(standard, vessel);
        await context.SaveChangesAsync();
        await GenerateMatrix(context, userContext, vessel.Id, 2, 2);

        await Should.ThrowAsync<ValidationException>(() =>
            GenerateMatrix(context, userContext, vessel.Id, 2, 2));

        context.VesselDeckLayouts.Count(x => x.VesselId == vessel.Id).ShouldBe(1);
    }

    [Test]
    public async Task ConfigureDoesNotRequireService()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.AddRange(standard, vessel);
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
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.AddRange(standard, vessel);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            GenerateMatrix(context, userContext, vessel.Id, 2, 2));

        context.VesselDeckLayouts.ShouldBeEmpty();
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
