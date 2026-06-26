using NUnit.Framework;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Seats;

public class SeatSupportTests
{
    [Test]
    public void CreateBoatSeatsDtoKeepsRegisteredSeatCountAsTotalSeats()
    {
        var boatId = Guid.NewGuid();
        var boat = new Boat
        {
            Id = boatId,
            Code = "WB01",
            Name = "WaterBus 01",
            SeatCount = 3,
            SeatsConfigured = true
        };
        var seats = new List<Seat>
        {
            Seat(boatId, 1, "1-A1", isActive: true),
            Seat(boatId, 2, "1-A2", isActive: false),
            Seat(boatId, 3, "1-A3", isActive: true)
        };

        var dto = SeatSupport.CreateBoatSeatsDto(boat, seats);

        dto.TotalSeats.ShouldBe(3);
        dto.ActiveSeats.ShouldBe(2);
        dto.ConfiguredSeats.ShouldBe(3);
        dto.SeatsConfigured.ShouldBeTrue();
    }

    [Test]
    public void CreateBoatSeatsDtoReturnsCompactLayoutWithoutChangingSeatCounts()
    {
        var boatId = Guid.NewGuid();
        var boat = new Boat
        {
            Id = boatId,
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
            .Select(id => Seat(boatId, id, $"1-A{id}", isActive: true))
            .ToList();

        var dto = SeatSupport.CreateBoatSeatsDto(boat, seats, deckLayouts);

        dto.TotalSeats.ShouldBe(80);
        dto.ConfiguredSeats.ShouldBe(80);
        dto.ActiveSeats.ShouldBe(80);
        dto.Decks.Single().RowCount.ShouldBe(20);
        dto.Decks.Single().ColumnCount.ShouldBe(8);
        dto.Decks.Single().Cells.Count.ShouldBe(160);
    }

    private static Seat Seat(Guid boatId, int column, string code, bool isActive) =>
        new()
        {
            Id = Guid.NewGuid(),
            BoatId = boatId,
            Code = code,
            Deck = 1,
            Row = "A",
            Column = column,
            IsActive = isActive
        };
}
