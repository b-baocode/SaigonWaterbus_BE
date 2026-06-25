using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Application.Vessels;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Vessels;

public class VesselSeatFlowIntegrationTests
{
    [TestCase(SeatSetupType.FullStandard)]
    [TestCase(SeatSetupType.StandardAndVip)]
    public async Task CreateVesselDoesNotRequireService(SeatSetupType setupType)
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);

        var result = await CreateVesselUseCase(context, userContext).ExecuteAsync(
            CreateRequest(setupType),
            CancellationToken.None);

        result.SeatSetupType.ShouldBe(setupType);
        result.Status.ShouldBe(VesselStatus.Inactive);
        result.SeatsConfigured.ShouldBeFalse();
    }

    [Test]
    public async Task UpdateSeatSetupTypeBeforeLayoutIsAllowed()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.Add(vessel);
        await context.SaveChangesAsync();

        var result = await UpdateVesselUseCase(context, userContext).ExecuteAsync(
            new UpdateVesselRequest(
                vessel.Id,
                SeatSetupType: SeatSetupType.StandardAndVip),
            CancellationToken.None);

        result.SeatSetupType.ShouldBe(SeatSetupType.StandardAndVip);
    }

    [Test]
    public async Task UpdateSeatSetupTypeRejectsVesselWithExistingLayout()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(
            SeatSetupType.FullStandard,
            seatsConfigured: true);
        AddSeats(vessel, vessel.SeatCount);
        context.Add(vessel);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ValidationException>(() =>
            UpdateVesselUseCase(context, userContext).ExecuteAsync(
                new UpdateVesselRequest(
                    vessel.Id,
                    SeatSetupType: SeatSetupType.StandardAndVip),
                CancellationToken.None));

        vessel.SeatSetupType.ShouldBe(SeatSetupType.FullStandard);
    }

    [Test]
    public async Task UpdateVesselUpsertsRentalPricesWithoutRemovingExistingPrices()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        vessel.HourlyRentalPrice = 2000000m;
        vessel.DailyRentalPrice = 15000000m;
        vessel.Currency = "VND";
        context.Add(vessel);
        await context.SaveChangesAsync();

        var result = await UpdateVesselUseCase(context, userContext).ExecuteAsync(
            new UpdateVesselRequest(
                vessel.Id,
                RentalPrices:
                [
                    new VesselRentalPriceRequest(
                        VesselRentalUnit.Hour,
                        2500000m,
                        "usd",
                        "Gia gio moi")
                ]),
            CancellationToken.None);

        result.RentalPrices.Count.ShouldBe(2);
        var hourlyPrice = result.RentalPrices.Single(x => x.RentalUnit == VesselRentalUnit.Hour);
        hourlyPrice.UnitPrice.ShouldBe(2500000m);
        hourlyPrice.Currency.ShouldBe("USD");
        hourlyPrice.Note.ShouldBeNull();

        var dailyPrice = result.RentalPrices.Single(x => x.RentalUnit == VesselRentalUnit.Day);
        dailyPrice.UnitPrice.ShouldBe(15000000m);
        dailyPrice.Currency.ShouldBe("USD");
        dailyPrice.Note.ShouldBeNull();
    }

    [Test]
    public async Task ActivateRejectsVesselWithoutConfiguredSeats()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.StandardAndVip);
        context.Add(vessel);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ValidationException>(() =>
            new UpdateVesselStatusRequestUseCase(context, userContext).ExecuteAsync(
                new UpdateVesselStatusRequest(vessel.Id, VesselStatus.Active),
                CancellationToken.None));

        vessel.Status.ShouldBe(VesselStatus.Inactive);
    }

    [Test]
    public async Task ActivateAcceptsConfiguredVesselWithoutService()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(
            SeatSetupType.StandardAndVip,
            seatsConfigured: true);
        AddSeats(vessel, vessel.SeatCount);
        context.Add(vessel);
        await context.SaveChangesAsync();

        var result = await new UpdateVesselStatusRequestUseCase(context, userContext)
            .ExecuteAsync(
                new UpdateVesselStatusRequest(vessel.Id, VesselStatus.Active),
                CancellationToken.None);

        result.Status.ShouldBe(VesselStatus.Active);
        result.IsReadyForOperation.ShouldBeTrue();
    }

    [Test]
    public async Task GetVesselsCanSearchByVesselId()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.StandardAndVip);
        context.Add(vessel);
        await context.SaveChangesAsync();

        var result = await new GetVesselsRequestUseCase(context, userContext)
            .ExecuteAsync(new GetVesselsRequest(Search: vessel.Id.ToString()), CancellationToken.None);

        result.Single().Id.ShouldBe(vessel.Id);
    }

    [Test]
    public async Task CreateVesselStoresPrimaryImageUrl()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);

        var result = await CreateVesselUseCase(context, userContext).ExecuteAsync(
            new CreateVesselRequest(
                $"IMG_{Guid.NewGuid():N}"[..20],
                "Image vessel",
                VesselStatus.Inactive,
                4,
                1,
                SeatSetupType: SeatSetupType.FullStandard,
                ImageUrls:
                [
                    "https://example.test/vessels/main.jpg",
                    "https://example.test/vessels/deck.jpg"
                ]),
            CancellationToken.None);

        result.ImageUrl.ShouldBe("https://example.test/vessels/main.jpg");
        result.ImageUrls.ShouldBe(["https://example.test/vessels/main.jpg"]);
    }

    [Test]
    public async Task UpdateVesselReplacesImageUrls()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        vessel.ImageUrl = "https://example.test/vessels/old-main.jpg";
        context.Add(vessel);
        await context.SaveChangesAsync();

        var result = await UpdateVesselUseCase(context, userContext).ExecuteAsync(
            new UpdateVesselRequest(
                vessel.Id,
                ImageUrl: "https://example.test/vessels/new-main.jpg"),
            CancellationToken.None);

        result.ImageUrl.ShouldBe("https://example.test/vessels/new-main.jpg");
        result.ImageUrls.ShouldBe(["https://example.test/vessels/new-main.jpg"]);
    }

    [Test]
    public async Task UpdateVesselAcceptsSwaggerPayloadWithImageUrl()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = new Vessel
        {
            Code = "WB_005",
            Name = "Waterbus 05",
            Status = VesselStatus.Inactive,
            SeatCount = 80,
            NumberOfDecks = 1,
            SeatSetupType = SeatSetupType.FullStandard,
            RegistrationNumber = "VN-005",
            ImageUrl = "https://example.test/vessels/old-main.jpg"
        };
        context.Add(vessel);
        await context.SaveChangesAsync();

        var result = await UpdateVesselUseCase(context, userContext).ExecuteAsync(
            new UpdateVesselRequest(
                vessel.Id,
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
                    new VesselRentalPriceRequest(VesselRentalUnit.Hour, 10m, "VND", "abc"),
                    new VesselRentalPriceRequest(VesselRentalUnit.Day, 20m, "VND", "abc")
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
        result.RentalPrices.Single(x => x.RentalUnit == VesselRentalUnit.Hour).UnitPrice.ShouldBe(10m);
        result.RentalPrices.Single(x => x.RentalUnit == VesselRentalUnit.Day).UnitPrice.ShouldBe(20m);
    }

    private static CreateVesselRequestUseCase CreateVesselUseCase(
        Infrastructure.Data.ApplicationDbContext context,
        TestUserContext userContext) =>
        new(
            context,
            userContext,
            new TestDatabaseExceptionClassifier(),
            new TestVesselImageStorageService());

    private static UpdateVesselRequestUseCase UpdateVesselUseCase(
        Infrastructure.Data.ApplicationDbContext context,
        TestUserContext userContext) =>
        new(
            context,
            userContext,
            new TestDatabaseExceptionClassifier(),
            new TestVesselImageStorageService());

    private static CreateVesselRequest CreateRequest(SeatSetupType setupType) =>
        new(
            $"NEW_{Guid.NewGuid():N}"[..20],
            "New vessel",
            VesselStatus.Inactive,
            4,
            1,
            SeatSetupType: setupType);

    private static void AddSeats(Vessel vessel, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var row = SeatSupport.RowLabel(index / 10);
            var column = (index % 10) + 1;
            vessel.Seats.Add(new Seat
            {
                VesselId = vessel.Id,
                Code = SeatSupport.SeatCode(1, row, column),
                SeatTypeName = SeatSupport.StandardSeatTypeName,
                Deck = 1,
                Row = row,
                Column = column,
                IsActive = true
            });
        }
    }
}
