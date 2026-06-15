using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Application.WaterbusServices;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.WaterbusServices;

public class ServiceSeatPriceFlowIntegrationTests
{
    [TestCase("STANDARD", 1)]
    [TestCase("VIP", 1.5)]
    public async Task CanDisableSeatPriceBecauseVesselsAreNotAssignedToServices(
        string seatTypeCode,
        decimal modifier)
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vip = SeatFlowTestData.SeatType("VIP");
        var service = SeatFlowTestData.Service(
            "SERVICE",
            (standard, 1m, true),
            (vip, 1.5m, true));
        context.AddRange(standard, vip, service);
        await context.SaveChangesAsync();

        var result = await UseCase(context, userContext).ExecuteAsync(
            new UpdateWaterbusServiceSeatPriceRequest(
                service.Id,
                seatTypeCode,
                modifier,
                false),
            CancellationToken.None);

        result.SeatTypes.Single(x => x.Code == seatTypeCode).IsActive.ShouldBeFalse();
    }

    [Test]
    public async Task CannotActivateGloballyInactiveSeatType()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vip = SeatFlowTestData.SeatType("VIP", isActive: false);
        var service = SeatFlowTestData.Service("DAY", (standard, 1m, true));
        context.AddRange(standard, vip, service);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ValidationException>(() =>
            UseCase(context, userContext).ExecuteAsync(
                new UpdateWaterbusServiceSeatPriceRequest(
                    service.Id,
                    "VIP",
                    1.5m,
                    true),
                CancellationToken.None));

        context.ServiceSeatTypePrices
            .ShouldNotContain(x => x.SeatTypeId == vip.Id);
    }

    [Test]
    public async Task CanAddVipPrice()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vip = SeatFlowTestData.SeatType("VIP");
        var service = SeatFlowTestData.Service("NIGHT", (standard, 1m, true));
        context.AddRange(standard, vip, service);
        await context.SaveChangesAsync();

        var result = await UseCase(context, userContext).ExecuteAsync(
            new UpdateWaterbusServiceSeatPriceRequest(
                service.Id,
                "VIP",
                1.75m,
                true),
            CancellationToken.None);

        var vipResult = result.SeatTypes.Single(x => x.Code == "VIP");
        vipResult.PriceModifier.ShouldBe(1.75m);
        vipResult.IsActive.ShouldBeTrue();
    }

    [Test]
    public async Task CanChangeVipModifier()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var standard = SeatFlowTestData.SeatType("STANDARD");
        var vip = SeatFlowTestData.SeatType("VIP");
        var service = SeatFlowTestData.Service(
            "NIGHT",
            (standard, 1m, true),
            (vip, 1.5m, true));
        context.AddRange(standard, vip, service);
        await context.SaveChangesAsync();

        var result = await UseCase(context, userContext).ExecuteAsync(
            new UpdateWaterbusServiceSeatPriceRequest(
                service.Id,
                "VIP",
                2m,
                true),
            CancellationToken.None);

        result.SeatTypes.Single(x => x.Code == "VIP")
            .PriceModifier.ShouldBe(2m);
    }

    private static UpdateWaterbusServiceSeatPriceRequestUseCase UseCase(
        Infrastructure.Data.ApplicationDbContext context,
        TestUserContext userContext) =>
        new(context, userContext);
}
