using SaigonWaterbus.Application.WaterbusServices;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.WaterbusServices;

public class WaterbusServiceSupportTests
{
    private static readonly Guid PublicServiceId = TestGuid(1);
    private static readonly Guid TouristServiceId = TestGuid(2);

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
            Id = PublicServiceId,
            Code = "PUBLIC",
            Name = "WaterBus cong cong",
            Description = "Dich vu theo tuyen.",
            IsActive = true,
            DisplayOrder = 1
        };

        var dto = WaterbusServiceSupport.CreateDto(service);

        dto.Id.ShouldBe(PublicServiceId);
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
            WaterbusService(PublicServiceId, "PUBLIC", true),
            WaterbusService(TouristServiceId, "TOURIST", false)
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
            WaterbusService(PublicServiceId, "PUBLIC", true),
            WaterbusService(TouristServiceId, "TOURIST", false)
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
            WaterbusService(PublicServiceId, "PUBLIC", true),
            WaterbusService(TouristServiceId, "TOURIST", false)
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

    private static Guid TestGuid(int id) =>
        Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}");
}
