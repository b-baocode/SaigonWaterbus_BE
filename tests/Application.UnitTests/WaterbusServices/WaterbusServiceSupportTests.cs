using SaigonWaterbus.Application.WaterbusServices;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
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
        var service = new WaterbusService
        {
            Id = 7,
            Code = "PUBLIC",
            Name = "WaterBus cong cong",
            Description = "Dich vu theo tuyen.",
            IsActive = true,
            DisplayOrder = 1
        };

        var dto = WaterbusServiceSupport.CreateDto(service);

        dto.Id.ShouldBe(7);
        dto.Code.ShouldBe("PUBLIC");
        dto.Name.ShouldBe("WaterBus cong cong");
        dto.Description.ShouldBe("Dich vu theo tuyen.");
        dto.IsActive.ShouldBeTrue();
        dto.DisplayOrder.ShouldBe(1);
    }

    [Test]
    public void ApplyVisibilityFilterAllowsAdminToSeeInactiveServicesForGetAll()
    {
        var admin = UserWithRole(Roles.AdminName);
        var services = new[]
        {
            WaterbusService(1, "PUBLIC", true),
            WaterbusService(2, "TOURIST", false)
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
            WaterbusService(1, "PUBLIC", true),
            WaterbusService(2, "TOURIST", false)
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
            WaterbusService(1, "PUBLIC", true),
            WaterbusService(2, "TOURIST", false)
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

    private static WaterbusService WaterbusService(int id, string code, bool isActive) =>
        new()
        {
            Id = id,
            Code = code,
            Name = code,
            IsActive = isActive
        };
}
