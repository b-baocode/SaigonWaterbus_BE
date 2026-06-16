using NUnit.Framework;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Application.WaterbusServices;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.WaterbusServices;

public class PublicWaterbusServiceCatalogTests
{
    [Test]
    public async Task ReturnsIdsAndSupportedSeatSetupsForActiveServices()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vip = SeatFlowTestData.SeatType("VIP");
        var service = SeatFlowTestData.Service(
            "TOURIST",
            (standard, 1m, true),
            (vip, 1.5m, true));
        context.AddRange(standard, vip, service);
        await context.SaveChangesAsync();

        var result = await new GetPublicWaterbusServiceCatalogRequestUseCase(context)
            .ExecuteAsync(CancellationToken.None);

        var item = result.ShouldHaveSingleItem();
        item.ServiceId.ShouldBe(service.Id);
        item.BookingMode.ShouldBe(BookingMode.SeatBased);
        item.SupportedSeatSetupTypes.ShouldBe(
            [SeatSetupType.FullStandard, SeatSetupType.StandardAndVip]);
        item.SeatTypes.Select(x => x.Code).ShouldBe(["STANDARD", "VIP"]);
        item.SeatTypes.Single(x => x.Code == "VIP").PriceModifier.ShouldBe(1.5m);
    }

    [Test]
    public async Task HidesInactiveServicesPricesAndGlobalSeatTypes()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var inactiveVip = SeatFlowTestData.SeatType("VIP", isActive: false);
        var activeService = SeatFlowTestData.Service(
            "PUBLIC",
            (standard, 1m, true),
            (inactiveVip, 1.5m, true));
        var inactiveService = SeatFlowTestData.Service("HIDDEN", (standard, 1m, true));
        inactiveService.IsActive = false;
        context.AddRange(standard, inactiveVip, activeService, inactiveService);
        await context.SaveChangesAsync();

        var result = await new GetPublicWaterbusServiceCatalogRequestUseCase(context)
            .ExecuteAsync(CancellationToken.None);

        var item = result.ShouldHaveSingleItem();
        item.Code.ShouldBe("PUBLIC");
        item.SeatTypes.Select(x => x.Code).ShouldBe(["STANDARD"]);
        item.SupportedSeatSetupTypes.ShouldBe([SeatSetupType.FullStandard]);
    }

    [Test]
    public async Task HidesActiveServiceWithoutAnActiveSeatType()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var readyService = SeatFlowTestData.Service("READY", (standard, 1m, true));
        var incompleteService = SeatFlowTestData.Service("INCOMPLETE");
        context.AddRange(standard, readyService, incompleteService);
        await context.SaveChangesAsync();

        var result = await new GetPublicWaterbusServiceCatalogRequestUseCase(context)
            .ExecuteAsync(CancellationToken.None);

        result.ShouldHaveSingleItem().Code.ShouldBe("READY");
    }
}
