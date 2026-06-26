using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Seats;

public class SeatLayoutPlannerTests
{
    [TestCase(SeatSetupType.FullStandard, null, "Standard")]
    [TestCase(SeatSetupType.FullStandard, "STANDARD", "Standard")]
    [TestCase(SeatSetupType.StandardAndVip, null, "Cabin")]
    [TestCase(SeatSetupType.StandardAndVip, "CABIN", "Cabin")]
    [TestCase(SeatSetupType.StandardAndVip, "RIVER", "River")]
    [TestCase(SeatSetupType.StandardAndVip, "SKY", "Sky")]
    public void NormalizeSeatTypeNameUsesCompactSeatTypeColumn(
        SeatSetupType setupType,
        string? code,
        string expected)
    {
        SeatSupport.NormalizeSeatTypeName(code, setupType).ShouldBe(expected);
    }

    [Test]
    public void NormalizeSeatTypeNameRejectsCabinForFullStandardBoat()
    {
        Should.Throw<ValidationException>(() =>
            SeatSupport.NormalizeSeatTypeName("CABIN", SeatSetupType.FullStandard));
    }

    [Test]
    public void NormalizeSeatTypeNameRejectsStandardForSightseeingBoat()
    {
        Should.Throw<ValidationException>(() =>
            SeatSupport.NormalizeSeatTypeName("STANDARD", SeatSetupType.StandardAndVip));
    }
}
