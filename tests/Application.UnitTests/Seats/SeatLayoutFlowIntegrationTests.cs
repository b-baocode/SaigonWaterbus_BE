using NUnit.Framework;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Seats;

public class SeatLayoutFlowIntegrationTests
{
    [Test]
    public void BoatSeatsDtoReportsConfiguredWhenSeatCountMatches()
    {
        var boat = new Boat
        {
            Id = Guid.NewGuid(),
            SeatCount = 4,
            NumberOfDecks = 1,
            SeatSetupType = SeatSetupType.FullStandard
        };
        var seats = new List<Seat>
        {
            Seat(boat.Id, "1-A1", 1, "A", 1),
            Seat(boat.Id, "1-A2", 1, "A", 2),
            Seat(boat.Id, "1-B1", 1, "B", 1),
            Seat(boat.Id, "1-B2", 1, "B", 2)
        };

        var dto = SeatSupport.CreateBoatSeatsDto(boat, seats);

        dto.ConfiguredSeats.ShouldBe(4);
        dto.ActiveSeats.ShouldBe(4);
        dto.SeatsConfigured.ShouldBeTrue();
        dto.Decks.Single().Rows.Count.ShouldBe(2);
    }

    [Test]
    public void BoatSeatsDtoMapsSeatTypeFromSeatTypeColumn()
    {
        var boat = new Boat
        {
            Id = Guid.NewGuid(),
            SeatCount = 1,
            NumberOfDecks = 1,
            SeatSetupType = SeatSetupType.StandardAndVip
        };
        var seats = new List<Seat>
        {
            Seat(boat.Id, "1-A1", 1, "A", 1, "CABIN", "Cabin")
        };

        var dto = SeatSupport.CreateBoatSeatsDto(boat, seats);

        dto.Decks.Single().Rows.Single().Seats.Single().SeatType!.SeatTypeCode.ShouldBe("CABIN");
    }

    private static Seat Seat(
        Guid boatId,
        string code,
        int deck,
        string row,
        int column,
        string seatTypeCode = "STANDARD",
        string seatTypeName = "Standard") =>
        new()
        {
            Id = Guid.NewGuid(),
            BoatId = boatId,
            Code = code,
            Deck = deck,
            Row = row,
            Column = column,
            SeatTypeCode = seatTypeCode,
            SeatTypeName = seatTypeName,
            IsActive = true
        };
}
