using NUnit.Framework;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Seats;

public class SeatLayoutPlannerTests
{
    [Test]
    public void EnsureDefaultSeatTypeCreatesStandardWhenMissing()
    {
        var seatTypes = new List<SeatType>();

        var result = SeatLayoutPlanner.EnsureSeatType(
            null,
            seatTypes,
            "STANDARD",
            "Standard Seat",
            1);

        result.Code.ShouldBe("STANDARD");
        result.IsActive.ShouldBeTrue();
        seatTypes.ShouldContain(result);
    }

    [Test]
    public void EnsureDefaultSeatTypeReactivatesExistingStandard()
    {
        var standard = new SeatType
        {
            Code = "standard",
            Name = "Standard Seat",
            DisplayOrder = 1,
            IsActive = false
        };
        var seatTypes = new List<SeatType> { standard };

        var result = SeatLayoutPlanner.EnsureSeatType(
            null,
            seatTypes,
            "STANDARD",
            "Standard Seat",
            1);

        result.ShouldBeSameAs(standard);
        result.IsActive.ShouldBeTrue();
        seatTypes.Count.ShouldBe(1);
    }

    [Test]
    public void EnsureSeatTypeCreatesSeededNonStandardTypeWhenMissing()
    {
        var seatTypes = new List<SeatType>();

        var result = SeatLayoutPlanner.EnsureSeatType(
            null,
            seatTypes,
            "RIVER",
            "River Seat",
            2);

        result.Code.ShouldBe("RIVER");
        result.IsActive.ShouldBeTrue();
        seatTypes.ShouldContain(result);
    }

    [TestCase(SeatSetupType.FullStandard, "STANDARD", true)]
    [TestCase(SeatSetupType.FullStandard, "VIP", false)]
    [TestCase(SeatSetupType.FullStandard, "CABIN", false)]
    [TestCase(SeatSetupType.FullStandard, "RIVER", false)]
    [TestCase(SeatSetupType.FullStandard, "SKY", false)]
    [TestCase(SeatSetupType.StandardAndVip, "STANDARD", true)]
    [TestCase(SeatSetupType.StandardAndVip, "CABIN", true)]
    [TestCase(SeatSetupType.StandardAndVip, "RIVER", true)]
    [TestCase(SeatSetupType.StandardAndVip, "SKY", true)]
    public void IsAllowedSeatTypeFollowsVesselSetup(
        SeatSetupType setupType,
        string code,
        bool expected)
    {
        SeatLayoutPlanner.IsAllowedSeatType(setupType, code).ShouldBe(expected);
    }
}
