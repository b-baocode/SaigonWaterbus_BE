using System.Text.Json;
using NUnit.Framework;
using SaigonWaterbus.Application.Trips;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Trips;

public class TripDtoSerializationTests
{
    [Test]
    public void TripStationFieldsAreLeanInJsonResponse()
    {
        var station = new TripRouteEndpointDto(
            Guid.NewGuid(),
            "ST-BD",
            "Bến Bạch Đằng",
            "https://example.test/station.jpg",
            ["https://example.test/station.jpg"],
            "10B Tôn Đức Thắng",
            10.1m,
            106.1m,
            true,
            true,
            true,
            PlannedDeparture: new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero));
        var stop = new TripStopDto(
            Guid.NewGuid(),
            station.StationId,
            station.StationName,
            "ST-BD",
            1,
            ScheduledArrival: null,
            ScheduledDeparture: new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero),
            ActualArrival: null,
            ActualDeparture: null,
            StopStatus: "Scheduled",
            StationImageUrl: "https://example.test/station.jpg",
            StationAddress: "10B Tôn Đức Thắng",
            AdjustedDeparture: new DateTimeOffset(2030, 1, 1, 8, 5, 0, TimeSpan.Zero));
        var detail = new TripDetailDto(
            Guid.NewGuid(),
            "TR-1",
            Guid.NewGuid(),
            "Route 1",
            "Regular",
            true,
            new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero),
            50,
            "Scheduled",
            null,
            [stop],
            Boat: new TripBoatDto(
                Guid.NewGuid(),
                "Waterbus 01",
                "WB-001",
                75,
                "Active",
                "https://example.test/boat.jpg",
                ["https://example.test/boat.jpg"],
                "SG-001",
                "Passenger",
                2,
                25,
                2024,
                "Boat description"),
            FromStation: station,
            ToStation: station);

        var json = JsonSerializer.Serialize(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.ShouldContain("stationId");
        json.ShouldContain("stationName");
        json.ShouldContain("scheduledDeparture");
        json.ShouldContain("adjustedDeparture");
        json.ShouldNotContain("stationCode");
        json.ShouldNotContain("imageUrl");
        json.ShouldNotContain("imageUrls");
        json.ShouldNotContain("address");
        json.ShouldNotContain("latitude");
        json.ShouldNotContain("longitude");
        json.ShouldNotContain("stationImageUrl");
        json.ShouldNotContain("stationAddress");
        json.ShouldContain("vesselId");
        json.ShouldContain("vesselName");
        json.ShouldNotContain("vesselCode");
        json.ShouldNotContain("registrationNumber");
        json.ShouldNotContain("maxSpeedKmh");
        json.ShouldNotContain("yearBuilt");
    }
}
