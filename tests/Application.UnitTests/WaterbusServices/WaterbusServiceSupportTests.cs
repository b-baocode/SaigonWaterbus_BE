using SaigonWaterbus.Application.WaterbusServices;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.WaterbusServices;

public class WaterbusServiceSupportTests
{
    [Test]
    public void NormalizeCodeTrimsAndUppercases()
    {
        WaterbusServiceSupport.NormalizeCode(" tourist ")
            .ShouldBe("TOURIST");
    }

    [Test]
    public void CreateDtoMapsWaterbusService()
    {
        var serviceId = Guid.NewGuid();
        var service = new WaterbusService
        {
            Id = serviceId,
            Code = "PUBLIC",
            Name = "WaterBus cong cong",
            Description = "Dich vu theo tuyen.",
            IsActive = true,
            DisplayOrder = 1
        };

        var dto = WaterbusServiceSupport.CreateDto(service);

        dto.Id.ShouldBe(serviceId);
        dto.Code.ShouldBe("PUBLIC");
        dto.Name.ShouldBe("WaterBus cong cong");
        dto.Description.ShouldBe("Dich vu theo tuyen.");
        dto.IsActive.ShouldBeTrue();
        dto.DisplayOrder.ShouldBe(1);
    }

    [Test]
    public void CreateSeatTypesDtoReturnsConfiguredPrices()
    {
        var service = WaterbusService(Guid.NewGuid(), "WB", true);
        service.SeatTypePrices =
        [
            ServicePrice(service, SeatType("VIP", 2), 1.5m),
            ServicePrice(service, SeatType("STANDARD", 1), 1m)
        ];

        var dto = WaterbusServiceSupport.CreateSeatTypesDto(service, includeInactive: false);

        dto.SeatTypes.Select(x => x.Code).ShouldBe(["STANDARD", "VIP"]);
        dto.SeatTypes.Single(x => x.Code == "VIP").PriceModifier.ShouldBe(1.5m);
    }

    [Test]
    public void CreateSeatTypesDtoHidesInactivePricesForNonAdmin()
    {
        var service = WaterbusService(Guid.NewGuid(), "WS", true);
        service.SeatTypePrices =
        [
            ServicePrice(service, SeatType("STANDARD", 1), 1m),
            ServicePrice(service, SeatType("VIP", 2), 1.5m, isActive: false)
        ];

        var dto = WaterbusServiceSupport.CreateSeatTypesDto(service, includeInactive: false);

        dto.SeatTypes.Select(x => x.Code).ShouldBe(["STANDARD"]);
    }

    [Test]
    public void CreateSeatTypesDtoIncludesInactivePricesForAdmin()
    {
        var service = WaterbusService(Guid.NewGuid(), "WB", true);
        service.SeatTypePrices =
        [
            ServicePrice(service, SeatType("VIP", 2), 1.5m, isActive: false)
        ];

        var dto = WaterbusServiceSupport.CreateSeatTypesDto(service, includeInactive: true);

        dto.SeatTypes.Single().Code.ShouldBe("VIP");
        dto.SeatTypes.Single().IsActive.ShouldBeFalse();
    }

    [Test]
    public void CreateSeatTypesDtoShowsUnconfiguredGlobalTypesForAdmin()
    {
        var service = WaterbusService(Guid.NewGuid(), "WB", true);
        var standard = SeatType("STANDARD", 1);
        var vip = SeatType("VIP", 2);
        service.SeatTypePrices =
        [
            ServicePrice(service, standard, 1m)
        ];

        var dto = WaterbusServiceSupport.CreateSeatTypesDto(
            service,
            includeInactive: true,
            [standard, vip]);

        dto.SeatTypes.Single(x => x.Code == "VIP").PriceModifier.ShouldBeNull();
        dto.SeatTypes.Single(x => x.Code == "VIP").IsActive.ShouldBeFalse();
    }

    [Test]
    public void CreateSeatTypeCreatesGlobalDefinition()
    {
        var seatType = WaterbusServiceSupport.CreateSeatType("STANDARD", "Standard", 1);

        seatType.Code.ShouldBe("STANDARD");
        seatType.IsActive.ShouldBeTrue();
    }

    [Test]
    public void CreateServiceSeatTypePriceLinksServiceAndGlobalSeatType()
    {
        var service = WaterbusService(Guid.NewGuid(), "WS", true);
        var vip = SeatType("VIP", 2);

        var price = WaterbusServiceSupport.CreateServiceSeatTypePrice(service, vip, 1.5m);

        price.WaterbusService.ShouldBe(service);
        price.SeatType.ShouldBe(vip);
        price.PriceModifier.ShouldBe(1.5m);
    }

    [Test]
    public void ApplyVisibilityFilterAllowsAdminToSeeInactiveServicesForGetAll()
    {
        var admin = UserWithRole(Roles.AdminName);
        var services = new[]
        {
            WaterbusService(Guid.NewGuid(), "PUBLIC", true),
            WaterbusService(Guid.NewGuid(), "TOURIST", false)
        }.AsQueryable();

        var visible = WaterbusServiceSupport.ApplyVisibilityFilter(services, admin, includeInactive: true)
            .Select(x => x.Code)
            .ToArray();

        visible.ShouldBe(["PUBLIC", "TOURIST"]);
    }

    [Test]
    public void ApplyVisibilityFilterCanHideInactiveServicesWhenAdminRequestsActiveOnly()
    {
        var admin = UserWithRole(Roles.AdminName);
        var services = new[]
        {
            WaterbusService(Guid.NewGuid(), "PUBLIC", true),
            WaterbusService(Guid.NewGuid(), "TOURIST", false)
        }.AsQueryable();

        var visible = WaterbusServiceSupport.ApplyVisibilityFilter(services, admin, includeInactive: false)
            .Select(x => x.Code)
            .ToArray();

        visible.ShouldBe(["PUBLIC"]);
    }

    [Test]
    public void ApplyVisibilityFilterHidesInactiveServicesFromManagerEvenWhenIncludeInactiveIsRequested()
    {
        var manager = UserWithRole(Roles.ManagerSystemName);
        var services = new[]
        {
            WaterbusService(Guid.NewGuid(), "PUBLIC", true),
            WaterbusService(Guid.NewGuid(), "TOURIST", false)
        }.AsQueryable();

        var visible = WaterbusServiceSupport.ApplyVisibilityFilter(services, manager, includeInactive: true)
            .Select(x => x.Code)
            .ToArray();

        visible.ShouldBe(["PUBLIC"]);
    }

    private static User UserWithRole(string roleSystemName) =>
        new()
        {
            Role = new Role
            {
                SystemName = roleSystemName
            }
        };

    private static WaterbusService WaterbusService(Guid id, string code, bool isActive) =>
        new()
        {
            Id = id,
            Code = code,
            Name = code,
            IsActive = isActive
        };

    private static SeatType SeatType(
        string code,
        int displayOrder,
        bool isActive = true) =>
        new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            DisplayOrder = displayOrder,
            IsActive = isActive
        };

    private static ServiceSeatTypePrice ServicePrice(
        WaterbusService service,
        SeatType seatType,
        decimal modifier,
        bool isActive = true) =>
        new()
        {
            WaterbusServiceId = service.Id,
            WaterbusService = service,
            SeatTypeId = seatType.Id,
            SeatType = seatType,
            PriceModifier = modifier,
            IsActive = isActive
        };
}
