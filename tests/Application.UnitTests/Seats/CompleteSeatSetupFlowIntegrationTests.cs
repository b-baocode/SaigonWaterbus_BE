using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
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
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        SeatFlowTestData.AddRequiredDocuments(boat);
        boat.SeatCount = 80;
        context.Add(boat);
        await context.SaveChangesAsync();

        await GenerateMatrix(context, userContext, boat.Id, 11, 8);

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
            boat.Id,
            [new DeckConfigDto(1, 11, 8, Cells: cells)]);

        configured.ConfiguredSeats.ShouldBe(80);
        configured.SeatsConfigured.ShouldBeTrue();
        configured.Decks.Single().Cells.Count.ShouldBe(88);
        configured.Decks.Single().Cells.Count(x => x.Type == SeatLayoutCellType.Aisle).ShouldBe(8);
        boat.Status.ShouldBe(BoatStatus.Active);
        (await context.Seats.CountAsync(x => x.BoatId == boat.Id)).ShouldBe(80);

        var fetched = await new GetSeatsRequestUseCase(context, userContext)
            .ExecuteAsync(new GetSeatsRequest(boat.Id), CancellationToken.None);

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
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        boat.SeatCount = 80;
        context.Add(boat);
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
            boat.Id,
            [new DeckConfigDto(1, 14, 7, Cells: cells)]);

        configured.ConfiguredSeats.ShouldBe(80);
        configured.Decks.Single().Cells.Count(x => x.Type == SeatLayoutCellType.Seat).ShouldBe(80);
        (await context.Seats.CountAsync(x => x.BoatId == boat.Id)).ShouldBe(80);
    }

    [Test]
    public async Task ConfigureNumbersSeatsByActualSeatsAndSkipsAisleColumns()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        boat.SeatCount = 6;
        context.Add(boat);
        await context.SaveChangesAsync();

        var configured = await Configure(
            context,
            userContext,
            boat.Id,
            [
                new DeckConfigDto(
                    1,
                    1,
                    7,
                    Cells: [new LayoutCellConfigDto(1, 4, SeatLayoutCellType.Aisle)])
            ]);

        configured.ConfiguredSeats.ShouldBe(6);
        var seats = await context.Seats
            .Where(x => x.BoatId == boat.Id)
            .OrderBy(x => x.Column)
            .Select(x => new { x.Code, x.Column })
            .ToListAsync();
        seats.Select(x => x.Code).ShouldBe(["1-A1", "1-A2", "1-A3", "1-A4", "1-A5", "1-A6"]);
        seats.Select(x => x.Column).ShouldBe([1, 2, 3, 5, 6, 7]);
    }

    [Test]
    public async Task FullStandardFlowGeneratesMatrixConfiguresSeatsAndAutoActivates()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        SeatFlowTestData.AddRequiredDocuments(boat);
        context.Add(boat);
        await context.SaveChangesAsync();

        var matrix = await GenerateMatrix(context, userContext, boat.Id, 2, 2);

        matrix.ConfiguredSeats.ShouldBe(0);
        matrix.Decks.Single().RowCount.ShouldBe(2);
        matrix.Decks.Single().Cells.Count.ShouldBe(4);
        matrix.Decks.Single().Cells.ShouldAllBe(x => x.Type == SeatLayoutCellType.Empty);
        matrix.Decks.Single().Cells.ShouldAllBe(x => x.SeatType == null);
        (await context.Seats.CountAsync(x => x.BoatId == boat.Id)).ShouldBe(0);
        boat.Status.ShouldBe(BoatStatus.Inactive);

        var configured = await Configure(
            context,
            userContext,
            boat.Id,
            [new DeckConfigDto(1, 2, 2)]);

        configured.ConfiguredSeats.ShouldBe(4);
        configured.SeatsConfigured.ShouldBeTrue();
        boat.Status.ShouldBe(BoatStatus.Active);
        (await context.Seats
                .Where(x => x.BoatId == boat.Id)
                .ToListAsync())
            .ShouldAllBe(x => x.SeatTypeCode == "STANDARD");
    }

    [Test]
    public async Task SightseeingFlowPersistsMixedSeatTypesAndAutoActivates()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.StandardAndVip);
        SeatFlowTestData.AddRequiredDocuments(boat);
        boat.SeatCount = 3;
        context.Add(boat);
        await context.SaveChangesAsync();

        var matrix = await GenerateMatrix(context, userContext, boat.Id, 2, 2);
        matrix.Decks.Single().Cells.ShouldAllBe(x => x.Type == SeatLayoutCellType.Empty);
        matrix.Decks.Single().Cells.ShouldAllBe(x => x.SeatType == null);

        var configured = await Configure(
            context,
            userContext,
            boat.Id,
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
        boat.Status.ShouldBe(BoatStatus.Active);

        var seats = await context.Seats
            .Where(x => x.BoatId == boat.Id)
            .ToListAsync();
        seats.Count(x => x.SeatTypeCode == "RIVER").ShouldBe(1);
        seats.Count(x => x.SeatTypeCode == "SKY").ShouldBe(1);
        seats.Count(x => x.SeatTypeCode == "CABIN").ShouldBe(1);
    }

    [Test]
    public async Task GetSeatsReturnsConfiguredSeatTypesForSightseeingBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.StandardAndVip);
        boat.SeatCount = 6;
        context.Add(boat);
        await context.SaveChangesAsync();

        await GenerateMatrix(context, userContext, boat.Id, 3, 4);

        await Configure(
            context,
            userContext,
            boat.Id,
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
            .ExecuteAsync(new GetSeatsRequest(boat.Id), CancellationToken.None);

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
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        SeatFlowTestData.AddRequiredDocuments(boat);
        context.Add(boat);
        await context.SaveChangesAsync();

        var configured = await Configure(
            context,
            userContext,
            boat.Id,
            [new DeckConfigDto(1, 2, 2)]);

        configured.ConfiguredSeats.ShouldBe(4);
        configured.SeatsConfigured.ShouldBeTrue();
        boat.SeatsConfigured.ShouldBeTrue();
        boat.Status.ShouldBe(BoatStatus.Active);
        (await context.Seats.CountAsync(x => x.BoatId == boat.Id)).ShouldBe(4);
    }

    [Test]
    public async Task ConfigureUpdatesBoatSeatCountFromGeneratedSeats()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        SeatFlowTestData.AddRequiredDocuments(boat);
        context.Add(boat);
        await context.SaveChangesAsync();
        await GenerateMatrix(context, userContext, boat.Id, 2, 2);

        var configured = await Configure(
            context,
            userContext,
            boat.Id,
            [new DeckConfigDto(1, 1, 2)]);

        configured.TotalSeats.ShouldBe(2);
        configured.ConfiguredSeats.ShouldBe(2);
        boat.SeatCount.ShouldBe(2);
        boat.SeatsConfigured.ShouldBeTrue();
    }

    [Test]
    public async Task CustomerCanViewSeatsForActiveConfiguredBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        SeatFlowTestData.AddRequiredDocuments(boat);
        context.Add(boat);
        await context.SaveChangesAsync();

        await Configure(
            context,
            adminContext,
            boat.Id,
            [new DeckConfigDto(1, 2, 2)]);

        var fetched = await new GetSeatsRequestUseCase(context, customerContext)
            .ExecuteAsync(new GetSeatsRequest(boat.Id), CancellationToken.None);

        fetched.ConfiguredSeats.ShouldBe(4);
        fetched.ActiveSeats.ShouldBe(4);
        fetched.SeatsConfigured.ShouldBeTrue();
    }

    [Test]
    public async Task CustomerCannotViewSeatsForInactiveBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var boat = SeatFlowTestData.Boat(
            SeatSetupType.FullStandard,
            seatsConfigured: true,
            status: BoatStatus.Inactive);
        context.Add(boat);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<NotFoundException>(() =>
            new GetSeatsRequestUseCase(context, customerContext)
                .ExecuteAsync(new GetSeatsRequest(boat.Id), CancellationToken.None));
    }

    [Test]
    public async Task CustomerCannotViewSeatsForActiveUnconfiguredBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var boat = SeatFlowTestData.Boat(
            SeatSetupType.FullStandard,
            seatsConfigured: false,
            status: BoatStatus.Active);
        context.Add(boat);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<NotFoundException>(() =>
            new GetSeatsRequestUseCase(context, customerContext)
                .ExecuteAsync(new GetSeatsRequest(boat.Id), CancellationToken.None));
    }

    [Test]
    public async Task GenerateMatrixRejectsDeckCountDifferentFromBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        boat.NumberOfDecks = 2;
        context.Add(boat);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ValidationException>(() =>
            GenerateMatrix(context, userContext, boat.Id, 2, 2));

        context.Seats.ShouldBeEmpty();
    }

    [Test]
    public async Task GenerateMatrixRejectsBoatThatAlreadyHasSeats()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        SeatFlowTestData.AddRequiredDocuments(boat);
        context.Add(boat);
        await context.SaveChangesAsync();
        await GenerateMatrix(context, userContext, boat.Id, 2, 2);
        await Configure(context, userContext, boat.Id, [new DeckConfigDto(1, 2, 2)]);

        await Should.ThrowAsync<ValidationException>(() =>
            GenerateMatrix(context, userContext, boat.Id, 2, 2));

        (await context.Seats.CountAsync(x => x.BoatId == boat.Id)).ShouldBe(4);
    }

    [Test]
    public async Task ConfigureDoesNotRequireService()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        SeatFlowTestData.AddRequiredDocuments(boat);
        context.Add(boat);
        await context.SaveChangesAsync();
        await GenerateMatrix(context, userContext, boat.Id, 2, 2);

        var configured = await Configure(
            context,
            userContext,
            boat.Id,
            [new DeckConfigDto(1, 2, 2)]);

        configured.SeatsConfigured.ShouldBeTrue();
        boat.Status.ShouldBe(BoatStatus.Active);
    }

    [Test]
    public async Task ConfigureAutoActivatesAfterSeatSetup()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        SeatFlowTestData.AddRequiredDocuments(boat);
        context.Add(boat);
        await context.SaveChangesAsync();

        var configured = await Configure(
            context,
            userContext,
            boat.Id,
            [new DeckConfigDto(1, 2, 2)]);

        configured.SeatsConfigured.ShouldBeTrue();
        boat.Status.ShouldBe(BoatStatus.Active);
    }

    [Test]
    public async Task ManagerCannotGenerateSeatMatrix()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedManagerAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        context.Add(boat);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            GenerateMatrix(context, userContext, boat.Id, 2, 2));

        context.Seats.ShouldBeEmpty();
    }

    private static Task<BoatSeatsDto> GenerateMatrix(
        Infrastructure.Data.ApplicationDbContext context,
        TestUserContext userContext,
        Guid boatId,
        int rows,
        int columns) =>
        new GenerateSeatMatrixRequestUseCase(context, userContext).ExecuteAsync(
            new GenerateSeatMatrixRequest(
                boatId,
                [new DeckMatrixConfigDto(1, rows, columns)]),
            CancellationToken.None);

    private static Task<BoatSeatsDto> Configure(
        Infrastructure.Data.ApplicationDbContext context,
        TestUserContext userContext,
        Guid boatId,
        IReadOnlyCollection<DeckConfigDto> decks) =>
        new GenerateSeatsRequestUseCase(context, userContext).ExecuteAsync(
            new GenerateSeatsRequest(boatId, decks),
            CancellationToken.None);

}
