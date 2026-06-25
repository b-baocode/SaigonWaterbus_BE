using NUnit.Framework;
using SaigonWaterbus.Application.Stations;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Stations;

public class StationDtoTests
{
    [Test]
    public void FromReturnsEmptyManagersAndStaffSinceUserStationAssignmentWasRemoved()
    {
        var station = new Station
        {
            Id = Guid.NewGuid(),
            StationCode = "BD",
            StationName = "Bach Dang",
            Status = StationStatus.Active
        };

        var dto = StationDto.From(station);

        dto.Managers.ShouldBeEmpty();
        dto.Staff.ShouldBeEmpty();
    }
}
