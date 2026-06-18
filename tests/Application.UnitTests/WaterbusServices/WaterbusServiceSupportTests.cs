using NUnit.Framework;
using SaigonWaterbus.Application.WaterbusServices;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
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
    public void CreateSeatTypesDtoReturnsGlobalSeatTypes()
    {
        var service = WaterbusService(Guid.NewGuid(), "WB", true);
        var standard = SeatType("STANDARD", 1);
        var vip = SeatType("VIP", 2);

        var dto = WaterbusServiceSupport.CreateSeatTypesDto(
            service,
            includeInactive: false,
            [vip, standard]);

        dto.SeatTypes.Select(x => x.Code).ShouldBe(["STANDARD", "VIP"]);
    }

    [Test]
    public void CreateSeatTypesDtoHidesInactiveSeatTypesForNonAdmin()
    {
        var service = WaterbusService(Guid.NewGuid(), "WS", true);
        var standard = SeatType("STANDARD", 1);
        var vip = SeatType("VIP", 2, isActive: false);

        var dto = WaterbusServiceSupport.CreateSeatTypesDto(
            service,
            includeInactive: false,
            [standard, vip]);

        dto.SeatTypes.Select(x => x.Code).ShouldBe(["STANDARD"]);
    }

    [Test]
    public void CreateSeatTypesDtoIncludesInactiveSeatTypesForAdmin()
    {
        var service = WaterbusService(Guid.NewGuid(), "WB", true);
        var vip = SeatType("VIP", 2, isActive: false);

        var dto = WaterbusServiceSupport.CreateSeatTypesDto(
            service,
            includeInactive: true,
            [vip]);

        dto.SeatTypes.Single().Code.ShouldBe("VIP");
        dto.SeatTypes.Single().IsActive.ShouldBeFalse();
    }

    [Test]
    public void CreateSeatTypesDtoShowsGlobalTypesForAdmin()
    {
        var service = WaterbusService(Guid.NewGuid(), "WB", true);
        var standard = SeatType("STANDARD", 1);
        var vip = SeatType("VIP", 2);

        var dto = WaterbusServiceSupport.CreateSeatTypesDto(
            service,
            includeInactive: true,
            [standard, vip]);

        dto.SeatTypes.Select(x => x.Code).ShouldBe(["STANDARD", "VIP"]);
        dto.SeatTypes.Single(x => x.Code == "VIP").IsActive.ShouldBeTrue();
    }

    [Test]
    public void CreateSeatTypeCreatesGlobalDefinition()
    {
        var seatType = WaterbusServiceSupport.CreateSeatType("STANDARD", "Standard", 1);

        seatType.Code.ShouldBe("STANDARD");
        seatType.IsActive.ShouldBeTrue();
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

}
