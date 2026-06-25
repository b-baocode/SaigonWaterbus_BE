using NUnit.Framework;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Seats;

public class SeatSupportTests
{
    [Test]
    public void CreateVesselSeatsDtoKeepsRegisteredSeatCountAsTotalSeats()
    {
        var vesselId = Guid.NewGuid();
        var vessel = new Vessel
        {
            Id = vesselId,
            Code = "WB01",
            Name = "WaterBus 01",
            SeatCount = 3,
            SeatsConfigured = true
        };
        var seats = new List<Seat>
        {
            Seat(vesselId, 1, "1-A1", isActive: true),
            Seat(vesselId, 2, "1-A2", isActive: false),
            Seat(vesselId, 3, "1-A3", isActive: true)
        };

        var dto = SeatSupport.CreateVesselSeatsDto(vessel, seats);

        dto.TotalSeats.ShouldBe(3);
        dto.ActiveSeats.ShouldBe(2);
        dto.ConfiguredSeats.ShouldBe(3);
        dto.SeatsConfigured.ShouldBeTrue();
    }

    [Test]
    public void CreateVesselSeatsDtoReturnsCompactLayoutWithoutChangingSeatCounts()
    {
        var vesselId = Guid.NewGuid();
        var vessel = new Vessel
        {
            Id = vesselId,
            Code = "WB01",
            Name = "WaterBus 01",
            SeatCount = 80,
            SeatsConfigured = true
        };
        var deckLayouts = new List<SeatDeckLayout>
        {
            new(1, 20, 8)
        };
        var seats = Enumerable.Range(1, 80)
            .Select(id => Seat(vesselId, id, $"1-A{id}", isActive: true))
            .ToList();

        var dto = SeatSupport.CreateVesselSeatsDto(vessel, seats, deckLayouts);

        dto.TotalSeats.ShouldBe(80);
        dto.ConfiguredSeats.ShouldBe(80);
        dto.ActiveSeats.ShouldBe(80);
        dto.Decks.Single().RowCount.ShouldBe(20);
        dto.Decks.Single().ColumnCount.ShouldBe(8);
        dto.Decks.Single().Cells.Count.ShouldBe(160);
    }

    private static Seat Seat(Guid vesselId, int column, string code, bool isActive) =>
        new()
        {
            Id = Guid.NewGuid(),
            VesselId = vesselId,
            Code = code,
            Deck = 1,
            Row = "A",
            Column = column,
            IsActive = isActive
        };
}
