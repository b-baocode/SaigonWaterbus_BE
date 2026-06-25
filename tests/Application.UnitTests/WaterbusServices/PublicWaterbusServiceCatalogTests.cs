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
        var service = SeatFlowTestData.Service("TOURIST");
        service.BookingMode = BookingMode.SeatTypeBased;
        context.Add(service);
        await context.SaveChangesAsync();

        var result = await new GetPublicWaterbusServiceCatalogRequestUseCase(context)
            .ExecuteAsync(CancellationToken.None);

        var item = result.ShouldHaveSingleItem();
        item.ServiceId.ShouldBe(service.Id);
        item.BookingMode.ShouldBe(BookingMode.SeatTypeBased);
        item.SupportedSeatSetupTypes.ShouldBe(
            [SeatSetupType.FullStandard, SeatSetupType.StandardAndVip]);
        item.SeatTypes.Select(x => x.Code).ShouldBe(["CABIN", "RIVER", "SKY"]);
    }

    [Test]
    public async Task HidesInactiveServicesAndGlobalSeatTypes()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var activeService = SeatFlowTestData.Service("PUBLIC");
        var inactiveService = SeatFlowTestData.Service("HIDDEN");
        inactiveService.IsActive = false;
        context.AddRange(activeService, inactiveService);
        await context.SaveChangesAsync();

        var result = await new GetPublicWaterbusServiceCatalogRequestUseCase(context)
            .ExecuteAsync(CancellationToken.None);

        var item = result.ShouldHaveSingleItem();
        item.Code.ShouldBe("PUBLIC");
        item.SeatTypes.Select(x => x.Code).ShouldBe(["STANDARD"]);
        item.SupportedSeatSetupTypes.ShouldBe([SeatSetupType.FullStandard]);
    }

    [Test]
    public async Task ReturnsActiveServiceWithoutServiceSeatPriceConfiguration()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var readyService = SeatFlowTestData.Service("READY");
        var incompleteService = SeatFlowTestData.Service("INCOMPLETE");
        context.AddRange(readyService, incompleteService);
        await context.SaveChangesAsync();

        var result = await new GetPublicWaterbusServiceCatalogRequestUseCase(context)
            .ExecuteAsync(CancellationToken.None);

        result.Select(x => x.Code).ShouldBe(["INCOMPLETE", "READY"]);
    }
}
