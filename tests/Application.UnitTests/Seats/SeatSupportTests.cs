using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Domain.Entities;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Seats;

public class SeatSupportTests
{
    [Test]
    public void CreateVesselSeatsDtoKeepsRegisteredSeatCountAsTotalSeats()
    {
        var vessel = new Vessel
        {
            Id = 1,
            Code = "WB01",
            Name = "WaterBus 01",
            SeatCount = 3,
            SeatsConfigured = true
        };
        var seats = new List<Seat>
        {
            Seat(1, "1-A1", isActive: true),
            Seat(2, "1-A2", isActive: false),
            Seat(3, "1-A3", isActive: true)
        };

        var dto = SeatSupport.CreateVesselSeatsDto(vessel, seats);

        dto.TotalSeats.ShouldBe(3);
        dto.ActiveSeats.ShouldBe(2);
        dto.ConfiguredSeats.ShouldBe(3);
        dto.SeatsConfigured.ShouldBeTrue();
    }

    private static Seat Seat(int id, string code, bool isActive) =>
        new()
        {
            Id = id,
            VesselId = 1,
            Code = code,
            Deck = 1,
            Row = "A",
            Column = id,
            IsActive = isActive
        };
}
