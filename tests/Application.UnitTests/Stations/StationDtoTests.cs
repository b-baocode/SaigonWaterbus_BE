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
    public void FromReturnsActiveManagersAndStaffAssignedToStation()
    {
        var station = new Station
        {
            Id = Guid.NewGuid(),
            StationCode = "BD",
            StationName = "Bach Dang",
            Status = StationStatus.Active
        };
        var manager = User("M01", "Manager One", Roles.ManagerCode, Roles.ManagerSystemName);
        var staff = User("S01", "Staff One", Roles.StaffCode, Roles.StaffSystemName);
        var inactiveStaff = User("S02", "Staff Two", Roles.StaffCode, Roles.StaffSystemName, UserStatus.Suspended);
        station.UserAssignments.Add(new UserStationAssignment
        {
            UserId = manager.Id,
            User = manager,
            StationId = station.Id,
            Station = station,
            IsActive = true,
            IsPrimary = true
        });
        station.UserAssignments.Add(new UserStationAssignment
        {
            UserId = staff.Id,
            User = staff,
            StationId = station.Id,
            Station = station,
            IsActive = true
        });
        station.UserAssignments.Add(new UserStationAssignment
        {
            UserId = inactiveStaff.Id,
            User = inactiveStaff,
            StationId = station.Id,
            Station = station,
            IsActive = true
        });

        var dto = StationDto.From(station);

        dto.Managers.Count.ShouldBe(1);
        dto.Managers.Single().UserId.ShouldBe(manager.Id);
        dto.Managers.Single().IsPrimary.ShouldBeTrue();

        dto.Staff.Count.ShouldBe(1);
        dto.Staff.Single().UserId.ShouldBe(staff.Id);
    }

    private static User User(
        string code,
        string fullName,
        string roleCode,
        string roleSystemName,
        UserStatus status = UserStatus.Active) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserCode = code,
            FullName = fullName,
            Status = status,
            Role = new Role
            {
                Code = roleCode,
                SystemName = roleSystemName,
                DisplayName = roleSystemName
            }
        };
}
