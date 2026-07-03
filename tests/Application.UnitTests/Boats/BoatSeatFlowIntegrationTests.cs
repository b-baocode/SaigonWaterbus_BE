using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Application.Boats;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Boats;

public class BoatSeatFlowIntegrationTests
{
    [TestCase(SeatSetupType.FullStandard)]
    [TestCase(SeatSetupType.StandardAndVip)]
    public async Task CreateBoatDoesNotRequireService(SeatSetupType setupType)
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);

        var result = await CreateBoatUseCase(context, userContext).ExecuteAsync(
            CreateRequest(setupType),
            CancellationToken.None);

        result.SeatSetupType.ShouldBe(setupType);
        result.Status.ShouldBe(BoatStatus.Inactive);
        result.SeatsConfigured.ShouldBeFalse();
    }

    [Test]
    public async Task UpdateSeatSetupTypeBeforeLayoutIsAllowed()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        context.Add(boat);
        await context.SaveChangesAsync();

        var result = await UpdateBoatUseCase(context, userContext).ExecuteAsync(
            new UpdateBoatRequest(
                boat.Id,
                SeatSetupType: SeatSetupType.StandardAndVip),
            CancellationToken.None);

        result.SeatSetupType.ShouldBe(SeatSetupType.StandardAndVip);
    }

    [Test]
    public async Task UpdateSeatSetupTypeRejectsBoatWithExistingLayout()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(
            SeatSetupType.FullStandard,
            seatsConfigured: true);
        AddSeats(boat, boat.SeatCount);
        context.Add(boat);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ValidationException>(() =>
            UpdateBoatUseCase(context, userContext).ExecuteAsync(
                new UpdateBoatRequest(
                    boat.Id,
                    SeatSetupType: SeatSetupType.StandardAndVip),
                CancellationToken.None));

        boat.SeatSetupType.ShouldBe(SeatSetupType.FullStandard);
    }

    [Test]
    public async Task UpdateBoatUpsertsRentalPricesWithoutRemovingExistingPrices()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        boat.HourlyRentalPrice = 2000000m;
        boat.DailyRentalPrice = 15000000m;
        boat.Currency = "VND";
        context.Add(boat);
        await context.SaveChangesAsync();

        var result = await UpdateBoatUseCase(context, userContext).ExecuteAsync(
            new UpdateBoatRequest(
                boat.Id,
                RentalPrices:
                [
                    new BoatRentalPriceRequest(
                        BoatRentalUnit.Hour,
                        2500000m,
                        "usd",
                        "Gia gio moi")
                ]),
            CancellationToken.None);

        result.RentalPrices.Count.ShouldBe(2);
        var hourlyPrice = result.RentalPrices.Single(x => x.RentalUnit == BoatRentalUnit.Hour);
        hourlyPrice.UnitPrice.ShouldBe(2500000m);
        hourlyPrice.Currency.ShouldBe("USD");
        hourlyPrice.Note.ShouldBeNull();

        var dailyPrice = result.RentalPrices.Single(x => x.RentalUnit == BoatRentalUnit.Day);
        dailyPrice.UnitPrice.ShouldBe(15000000m);
        dailyPrice.Currency.ShouldBe("USD");
        dailyPrice.Note.ShouldBeNull();
    }

    [Test]
    public async Task ActivateRejectsBoatWithoutConfiguredSeats()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.StandardAndVip);
        context.Add(boat);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ValidationException>(() =>
            new UpdateBoatStatusRequestUseCase(context, userContext).ExecuteAsync(
                new UpdateBoatStatusRequest(boat.Id, BoatStatus.Active),
                CancellationToken.None));

        boat.Status.ShouldBe(BoatStatus.Inactive);
    }

    [Test]
    public async Task ActivateAcceptsConfiguredBoatWithoutService()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(
            SeatSetupType.StandardAndVip,
            seatsConfigured: true);
        AddSeats(boat, boat.SeatCount);
        context.Add(boat);
        await context.SaveChangesAsync();

        var result = await new UpdateBoatStatusRequestUseCase(context, userContext)
            .ExecuteAsync(
                new UpdateBoatStatusRequest(boat.Id, BoatStatus.Active),
                CancellationToken.None);

        result.Status.ShouldBe(BoatStatus.Active);
        result.IsReadyForOperation.ShouldBeTrue();
    }

    [Test]
    public async Task GetBoatsCanSearchByBoatId()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.StandardAndVip);
        context.Add(boat);
        await context.SaveChangesAsync();

        var result = await new GetBoatsRequestUseCase(context, userContext)
            .ExecuteAsync(new GetBoatsRequest(Search: boat.Id.ToString()), CancellationToken.None);

        result.Single().Id.ShouldBe(boat.Id);
    }

    [Test]
    public async Task CreateBoatStoresPrimaryImageUrl()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);

        var result = await CreateBoatUseCase(context, userContext).ExecuteAsync(
            new CreateBoatRequest(
                $"IMG_{Guid.NewGuid():N}"[..20],
                "Image boat",
                BoatStatus.Inactive,
                4,
                1,
                SeatSetupType: SeatSetupType.FullStandard,
                ImageUrls:
                [
                    "https://example.test/boats/main.jpg",
                    "https://example.test/boats/deck.jpg"
                ]),
            CancellationToken.None);

        result.ImageUrl.ShouldBe("https://example.test/boats/main.jpg");
        result.ImageUrls.ShouldBe(["https://example.test/boats/main.jpg"]);
    }

    [Test]
    public async Task CreateBoatUploadsImageFileAndStoresReturnedUrl()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        await using var file = CreateImageFile();

        var result = await CreateBoatUseCase(context, userContext).ExecuteAsync(
            new CreateBoatRequest(
                $"UPL_{Guid.NewGuid():N}"[..20],
                "Upload image boat",
                BoatStatus.Inactive,
                4,
                1,
                SeatSetupType: SeatSetupType.FullStandard,
                ImageFiles: [new BoatImageFileRequest("boat.jpg", "image/jpeg", file.Length, file)]),
            CancellationToken.None);

        var expectedUrl = $"https://example.test/boats/{result.Id}/{result.Id:N}";
        result.ImageUrl.ShouldBe(expectedUrl);
        result.ImageUrls.ShouldBe([expectedUrl]);

        var boat = await context.Boats.SingleAsync(x => x.Id == result.Id);
        boat.ImageUrl.ShouldBe(expectedUrl);
        boat.ImagePublicId.ShouldBe(result.Id.ToString("N"));
    }

    [Test]
    public async Task CreateBoatRejectsUnsupportedImageContentType()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        await using var file = CreateImageFile();

        var act = async () => await CreateBoatUseCase(context, userContext).ExecuteAsync(
            new CreateBoatRequest(
                $"BAD_{Guid.NewGuid():N}"[..20],
                "Invalid image boat",
                BoatStatus.Inactive,
                4,
                1,
                SeatSetupType: SeatSetupType.FullStandard,
                ImageFiles: [new BoatImageFileRequest("boat.pdf", "application/pdf", file.Length, file)]),
            CancellationToken.None);

        await act.ShouldThrowAsync<ValidationException>();
    }

    [Test]
    public async Task UpdateBoatReplacesImageUrls()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        boat.ImageUrl = "https://example.test/boats/old-main.jpg";
        context.Add(boat);
        await context.SaveChangesAsync();

        var result = await UpdateBoatUseCase(context, userContext).ExecuteAsync(
            new UpdateBoatRequest(
                boat.Id,
                ImageUrl: "https://example.test/boats/new-main.jpg"),
            CancellationToken.None);

        result.ImageUrl.ShouldBe("https://example.test/boats/new-main.jpg");
        result.ImageUrls.ShouldBe(["https://example.test/boats/new-main.jpg"]);
    }

    [Test]
    public async Task UpdateBoatUploadsImageFileAndStoresReturnedUrl()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        boat.ImageUrl = "https://example.test/boats/old-main.jpg";
        context.Add(boat);
        await context.SaveChangesAsync();
        await using var file = CreateImageFile();

        var result = await UpdateBoatUseCase(context, userContext).ExecuteAsync(
            new UpdateBoatRequest(
                boat.Id,
                ImageFiles: [new BoatImageFileRequest("new-boat.png", "image/png", file.Length, file)]),
            CancellationToken.None);

        var expectedUrl = $"https://example.test/boats/{boat.Id}/{boat.Id:N}";
        result.ImageUrl.ShouldBe(expectedUrl);
        result.ImageUrls.ShouldBe([expectedUrl]);

        var updatedBoat = await context.Boats.SingleAsync(x => x.Id == boat.Id);
        updatedBoat.ImageUrl.ShouldBe(expectedUrl);
        updatedBoat.ImagePublicId.ShouldBe(boat.Id.ToString("N"));
    }

    [Test]
    public async Task UpdateBoatAcceptsSwaggerPayloadWithImageUrl()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = new Boat
        {
            Code = "WB_005",
            Name = "Waterbus 05",
            Status = BoatStatus.Inactive,
            SeatCount = 80,
            NumberOfDecks = 1,
            SeatSetupType = SeatSetupType.FullStandard,
            RegistrationNumber = "VN-005",
            ImageUrl = "https://example.test/boats/old-main.jpg"
        };
        context.Add(boat);
        await context.SaveChangesAsync();

        var result = await UpdateBoatUseCase(context, userContext).ExecuteAsync(
            new UpdateBoatRequest(
                boat.Id,
                Code: "WB_006",
                Name: "Waterbus 06",
                SeatCount: 80,
                NumberOfDecks: 1,
                RegistrationNumber: "VN-006",
                MaxSpeedKmh: 50,
                YearBuilt: 2026,
                Description: "abcdef",
                ImageUrl: "https://i.pinimg.com/236x/bd/e3/14/bde3147fb7e955639478c55a0e050cd9.jpg",
                SeatSetupType: SeatSetupType.FullStandard,
                RentalPrices:
                [
                    new BoatRentalPriceRequest(BoatRentalUnit.Hour, 10m, "VND", "abc"),
                    new BoatRentalPriceRequest(BoatRentalUnit.Day, 20m, "VND", "abc")
                ]),
            CancellationToken.None);

        result.Code.ShouldBe("WB_006");
        result.Name.ShouldBe("Waterbus 06");
        result.SeatCount.ShouldBe(80);
        result.NumberOfDecks.ShouldBe(1);
        result.RegistrationNumber.ShouldBe("VN-006");
        result.MaxSpeedKmh.ShouldBe(50);
        result.YearBuilt.ShouldBe(2026);
        result.Description.ShouldBe("abcdef");
        result.ImageUrl.ShouldBe("https://i.pinimg.com/236x/bd/e3/14/bde3147fb7e955639478c55a0e050cd9.jpg");
        result.ImageUrls.ShouldBe(["https://i.pinimg.com/236x/bd/e3/14/bde3147fb7e955639478c55a0e050cd9.jpg"]);
        result.RentalPrices.Count.ShouldBe(2);
        result.RentalPrices.Single(x => x.RentalUnit == BoatRentalUnit.Hour).UnitPrice.ShouldBe(10m);
        result.RentalPrices.Single(x => x.RentalUnit == BoatRentalUnit.Day).UnitPrice.ShouldBe(20m);
    }

    private static CreateBoatRequestUseCase CreateBoatUseCase(
        Infrastructure.Data.ApplicationDbContext context,
        TestUserContext userContext) =>
        new(
            context,
            userContext,
            new TestDatabaseExceptionClassifier(),
            new TestBoatImageStorageService());

    private static UpdateBoatRequestUseCase UpdateBoatUseCase(
        Infrastructure.Data.ApplicationDbContext context,
        TestUserContext userContext) =>
        new(
            context,
            userContext,
            new TestDatabaseExceptionClassifier(),
            new TestBoatImageStorageService());

    private static CreateBoatRequest CreateRequest(SeatSetupType setupType) =>
        new(
            $"NEW_{Guid.NewGuid():N}"[..20],
            "New boat",
            BoatStatus.Inactive,
            4,
            1,
            SeatSetupType: setupType);

    private static void AddSeats(Boat boat, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var row = SeatSupport.RowLabel(index / 10);
            var column = (index % 10) + 1;
            boat.Seats.Add(new Seat
            {
                BoatId = boat.Id,
                Code = SeatSupport.SeatCode(1, row, column),
                SeatTypeCode = "STANDARD",
                Deck = 1,
                Row = row,
                Column = column,
                IsActive = true
            });
        }
    }

    private static MemoryStream CreateImageFile() => new([0x1, 0x2, 0x3, 0x4]);
}
