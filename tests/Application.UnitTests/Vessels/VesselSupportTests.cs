using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Vessels;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Vessels;

public class VesselSupportTests
{
    [Test]
    public void EnsureCanActivateRejectsSeatBasedVesselWithoutSeatSetup()
    {
        var vessel = Vessel(BookingMode.SeatBased, seatsConfigured: false);

        var exception = Should.Throw<ValidationException>(() =>
            VesselSupport.EnsureCanActivate(vessel, "Status"));

        exception.Errors.Keys.ShouldContain("status");
    }

    [Test]
    public void IsReadyForOperationRejectsRentalVesselWithoutSeatSetup()
    {
        var vessel = Vessel(BookingMode.VesselRental, seatsConfigured: false);

        VesselSupport.IsReadyForOperation(vessel).ShouldBeFalse();
    }

    [Test]
    public void IsReadyForOperationRequiresSeatSetupForEveryVessel()
    {
        VesselSupport.IsReadyForOperation(Vessel(BookingMode.SeatBased, seatsConfigured: false)).ShouldBeFalse();
        VesselSupport.IsReadyForOperation(Vessel(BookingMode.SeatBased, seatsConfigured: true)).ShouldBeTrue();
        VesselSupport.IsReadyForOperation(Vessel(BookingMode.VesselRental, seatsConfigured: true)).ShouldBeTrue();
    }

    private static Vessel Vessel(BookingMode bookingMode, bool seatsConfigured) =>
        new()
        {
            Id = Guid.NewGuid(),
            Code = "WB01",
            Name = "Waterbus 01",
            Status = VesselStatus.Active,
            SeatsConfigured = seatsConfigured,
            WaterbusService = new WaterbusService
            {
                Id = Guid.NewGuid(),
                Code = bookingMode == BookingMode.VesselRental ? "WT" : "WB",
                Name = bookingMode == BookingMode.VesselRental ? "WaterTaxi" : "Waterbus",
                BookingMode = bookingMode
            }
        };
}
