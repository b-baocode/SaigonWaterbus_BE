using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Seats;

public class SeatSupportTests
{
    private static readonly Guid VesselId = TestGuid(100);

    [Test]
    public void CreateVesselSeatsDtoKeepsRegisteredSeatCountAsTotalSeats()
    {
        var vessel = new Vessel
        {
            Id = VesselId,
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

    [Test]
    public void CreateVesselSeatsDtoReturnsLayoutAndFacilitiesWithoutChangingSeatCounts()
    {
        var vessel = new Vessel
        {
            Id = VesselId,
            Code = "WB01",
            Name = "WaterBus 01",
            SeatCount = 80,
            SeatsConfigured = true
        };
        var deckLayouts = new List<VesselDeckLayout>
        {
            new()
            {
                VesselId = VesselId,
                DeckNumber = 1,
                RowCount = 20,
                ColumnCount = 8
            }
        };
        var facilities = new List<VesselFacility>
        {
            new()
            {
                Id = TestGuid(10),
                VesselId = VesselId,
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
            .Select(id => Seat(id, $"1-A{id}", isActive: true))
            .ToList();

        var dto = SeatSupport.CreateVesselSeatsDto(vessel, seats, deckLayouts, facilities);

        dto.TotalSeats.ShouldBe(80);
        dto.ConfiguredSeats.ShouldBe(80);
        dto.ActiveSeats.ShouldBe(80);
        dto.Decks.Single().RowCount.ShouldBe(20);
        dto.Decks.Single().ColumnCount.ShouldBe(8);
        dto.Facilities.Single().Type.ShouldBe(VesselFacilityType.Toilet);
    }

    private static Seat Seat(int id, string code, bool isActive) =>
        new()
        {
            Id = TestGuid(id),
            VesselId = VesselId,
            Code = code,
            Deck = 1,
            Row = "A",
            Column = id,
            IsActive = isActive
        };

    private static Guid TestGuid(int id) =>
        Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}");
}
