using NUnit.Framework;
using SaigonWaterbus.Application.Stations;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Stations;

public class StationDtoTests
{
    [Test]
    public void FromIncludesOnlyActiveManagerAndStaffAssignments()
    {
        var activeManager = User(Roles.ManagerSystemName, UserStatus.Active, "Active Manager");
        var inactiveManager = User(Roles.ManagerSystemName, UserStatus.Suspended, "Suspended Manager");
        var activeStaff = User(Roles.StaffSystemName, UserStatus.Active, "Active Staff");
        var station = new Station
        {
            Id = Guid.NewGuid(),
            StationCode = "BD",
            StationName = "Bach Dang",
            Status = StationStatus.Active,
            UserAssignments =
            [
                Assignment(activeManager, isActive: true, isPrimary: true),
                Assignment(inactiveManager, isActive: true, isPrimary: false),
                Assignment(activeStaff, isActive: true, isPrimary: false),
                Assignment(User(Roles.ManagerSystemName, UserStatus.Active, "Inactive Assignment"), isActive: false, isPrimary: false)
            ]
        };

        var dto = StationDto.From(station);

        dto.Managers.Count.ShouldBe(1);
        dto.Managers.Single().UserId.ShouldBe(activeManager.Id);
        dto.Managers.Single().IsPrimary.ShouldBeTrue();
        dto.Staff.Count.ShouldBe(1);
        dto.Staff.Single().UserId.ShouldBe(activeStaff.Id);
    }

    [Test]
    public void FromIgnoresAssignmentsWithMissingUserOrRole()
    {
        var station = new Station
        {
            Id = Guid.NewGuid(),
            StationCode = "BD",
            StationName = "Bach Dang",
            Status = StationStatus.Active,
            UserAssignments =
            [
                new UserStationAssignment
                {
                    UserId = Guid.NewGuid(),
                    IsActive = true,
                    IsPrimary = false
                },
                Assignment(new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "Missing Role",
                    Status = UserStatus.Active
                }, isActive: true, isPrimary: false)
            ]
        };

        var dto = StationDto.From(station);

        dto.Managers.ShouldBeEmpty();
        dto.Staff.ShouldBeEmpty();
    }

    private static UserStationAssignment Assignment(User user, bool isActive, bool isPrimary) =>
        new()
        {
            UserId = user.Id,
            User = user,
            IsActive = isActive,
            IsPrimary = isPrimary,
            AssignedAt = DateTimeOffset.UtcNow
        };

    private static User User(string roleSystemName, UserStatus status, string fullName) =>
        new()
        {
            Id = Guid.NewGuid(),
            FullName = fullName,
            Status = status,
            Role = new Role
            {
                Id = Guid.NewGuid(),
                Code = roleSystemName,
                SystemName = roleSystemName,
                DisplayName = roleSystemName
            }
        };
}
