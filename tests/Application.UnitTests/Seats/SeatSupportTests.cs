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
    public void CreateVesselSeatsDtoReturnsLayoutAndFacilitiesWithoutChangingSeatCounts()
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
        var deckLayouts = new List<VesselDeckLayout>
        {
            new()
            {
                VesselId = vesselId,
                DeckNumber = 1,
                RowCount = 20,
                ColumnCount = 8
            }
        };
        var facilities = new List<VesselFacility>
        {
            new()
            {
                Id = Guid.NewGuid(),
                VesselId = vesselId,
                Type = VesselFacilityType.Toilet,
                Deck = 1,
                Row = "O",
                Column = 1,
                RowSpan = 1,
                ColumnSpan = 2,
                IsActive = true
            }
        };
        var seats = Enumerable.Range(1, 80)
            .Select(id => Seat(vesselId, id, $"1-A{id}", isActive: true))
            .ToList();

        var dto = SeatSupport.CreateVesselSeatsDto(vessel, seats, deckLayouts, facilities);

        dto.TotalSeats.ShouldBe(80);
        dto.ConfiguredSeats.ShouldBe(80);
        dto.ActiveSeats.ShouldBe(80);
        dto.Decks.Single().RowCount.ShouldBe(20);
        dto.Decks.Single().ColumnCount.ShouldBe(8);
        dto.Facilities.Single().Type.ShouldBe(VesselFacilityType.Toilet);
        dto.Decks.Single().Cells.Count.ShouldBe(160);
        dto.Decks.Single().Cells.Count(x => x.Type == SeatLayoutCellType.Toilet).ShouldBe(2);
        dto.Decks.Single().Cells.ShouldContain(x => x.Row == 15 && x.Column == 1 && x.Facility != null);
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
