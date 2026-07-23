using System.Text.Json;
using NUnit.Framework;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Routes;
using SaigonWaterbus.Application.Tickets;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.ApiContracts;

public class StationReferenceSerializationTests
{
    [Test]
    public void RouteStopsExposeStationIdAndNameOnly()
    {
        var stationId = Guid.NewGuid();
        var route = new RouteDetailDto(
            Guid.NewGuid(),
            "R1",
            "Route 1",
            "Regular",
            null,
            3m,
            15m,
            "Active",
            [new RouteStopDto(Guid.NewGuid(), stationId, "ST-BD", "Bến Bạch Đằng", 1, null, null, true, false)],
            null);

        var json = JsonSerializer.Serialize(route, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.ShouldContain("stationId");
        json.ShouldContain("stationName");
        json.ShouldNotContain("stationCode");
    }

    [Test]
    public void BookingAndCharterStationReferencesIncludeStationIds()
    {
        var fromStationId = Guid.NewGuid();
        var toStationId = Guid.NewGuid();
        var bookingItem = new BookingItemDto(
            Guid.NewGuid(),
            "TR-1",
            "Nguyen Van A",
            null,
            "Vé người lớn",
            "A1",
            "Bến A",
            "Bến B",
            null,
            null,
            10000m,
            "Confirmed",
            null,
            null,
            null,
            fromStationId,
            toStationId);
        var charterLeg = new CharterBookingRouteLegEstimateDto(
            1,
            fromStationId,
            "Bến A",
            toStationId,
            "Bến B",
            3m,
            15m);

        var json = JsonSerializer.Serialize(new { bookingItem, charterLeg }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.ShouldContain("fromStationId");
        json.ShouldContain("toStationId");
        json.ShouldContain(fromStationId.ToString());
        json.ShouldContain(toStationId.ToString());
    }

    [Test]
    public void BoatReferencesIncludeBoatIdAndAvoidDuplicateVesselName()
    {
        var boatId = Guid.NewGuid();
        var charter = new CharterBookingListItemDto(
            Guid.NewGuid(),
            "CB-1",
            "Confirmed",
            "Paid",
            "2030-01-01",
            "08:00:00",
            "Hour",
            1,
            1,
            0,
            1,
            "Bến A",
            "Bến B",
            "Waterbus 01",
            100000m,
            100000m,
            [],
            null,
            BoatId: boatId);
        var scan = new TicketScanDto(
            Guid.NewGuid(),
            "TK-1",
            "QR",
            null,
            null,
            "Active",
            new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero),
            null,
            null,
            null,
            null,
            null,
            null,
            Guid.NewGuid(),
            "BK-1",
            "SeatBooking",
            "Confirmed",
            "Paid",
            "Nguyen Van A",
            "0900000000",
            null,
            1,
            1,
            null,
            null,
            null,
            null,
            "TR-1",
            null,
            null,
            "Waterbus 01",
            "Waterbus 01",
            "Bến A",
            "Bến B",
            "A1",
            null,
            [],
            BoatId: boatId);

        var json = JsonSerializer.Serialize(new { charter, scan }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.ShouldContain("boatId");
        json.ShouldContain(boatId.ToString());
        json.ShouldContain("boatName");
        json.ShouldNotContain("vesselName");
    }
}
