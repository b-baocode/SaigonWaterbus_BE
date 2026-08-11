using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.TourGuide;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;

namespace SaigonWaterbus.Application.UnitTests.TourGuide;

/// <summary>
/// Khối ngữ cảnh này đi THẲNG vào system prompt và không có đường nào nhìn thấy nó từ API,
/// nên sai thì chỉ lộ ra qua câu trả lời sai của hướng dẫn viên. Chốt bằng test.
/// </summary>
public class TourGuideContextReaderTests
{
    [Test]
    public async Task Builds_trip_prompt_block_and_position()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"tour-guide-{Guid.NewGuid():N}")
            .Options;

        await using var context = new ApplicationDbContext(options);

        var bd = new Station { Id = Guid.NewGuid(), StationName = "Bạch Đằng", StationCode = "BD" };
        var tt = new Station { Id = Guid.NewGuid(), StationName = "Thủ Thiêm", StationCode = "TT" };
        var boat = new Boat { Id = Guid.NewGuid(), Name = "Greenlines A", Code = "BOAT-1" };
        var route = new Route { Id = Guid.NewGuid(), RouteName = "Tuyến đi số 1", RouteCode = "WB-1" };

        var tripId = Guid.NewGuid();
        var depart = new DateTimeOffset(2026, 8, 11, 1, 30, 0, TimeSpan.Zero); // 08:30 giờ VN

        var trip = new Trip
        {
            Id = tripId,
            TripCode = "BB-20260811-WB-1-0830",
            RouteId = route.Id,
            Route = route,
            BoatId = boat.Id,
            Boat = boat,
            DepartureTime = depart,
            ArrivalTime = depart.AddMinutes(45),
            TripStatus = TripStatus.InProgress,
            TripStops =
            [
                new TripStop
                {
                    Id = Guid.NewGuid(), TripId = tripId, StationId = bd.Id, Station = bd,
                    StopOrder = 1, PlannedArrivalTime = depart, ActualDepartureTime = depart,
                },
                new TripStop
                {
                    Id = Guid.NewGuid(), TripId = tripId, StationId = tt.Id, Station = tt,
                    StopOrder = 2, PlannedArrivalTime = depart.AddMinutes(45),
                },
            ],
        };

        var now = depart.AddMinutes(20);

        context.Stations.AddRange(bd, tt);
        context.Boats.Add(boat);
        context.Routes.Add(route);
        context.Trips.Add(trip);
        context.BoatLatestLocations.Add(new BoatLatestLocation
        {
            BoatId = boat.Id,
            GpsDeviceId = Guid.NewGuid(),
            TripId = tripId,
            NextStationId = tt.Id,
            NextStation = tt,
            Latitude = 10.775m,
            Longitude = 106.705m,
            Heading = 45,
            RemainingMinutesToNextStation = 25,
            RecordedAt = now.AddSeconds(-30),
            Sequence = 1,
        });
        await context.SaveChangesAsync();

        var reader = new TourGuideContextReader(
            context, new FakeTime(now));

        var result = await reader.ReadTripAsync(tripId, CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Position, Is.Not.Null);
        Assert.That(result.Position!.Latitude, Is.EqualTo(10.775).Within(0.001));
        Assert.That(result.Position.Heading, Is.EqualTo(45));

        Assert.That(result.PromptBlock, Does.Contain("Tuyến đi số 1"));
        Assert.That(result.PromptBlock, Does.Contain("Tàu Greenlines A"));
        Assert.That(result.PromptBlock, Does.Contain("8:30"));
        Assert.That(result.PromptBlock, Does.Contain("đang chạy"));
        Assert.That(result.PromptBlock, Does.Contain("1. Bạch Đằng"));
        Assert.That(result.PromptBlock, Does.Contain("đã rời bến"));
        Assert.That(result.PromptBlock, Does.Contain("Bến kế tiếp: Thủ Thiêm"));
        Assert.That(result.PromptBlock, Does.Contain("còn khoảng 25 phút"));
    }

    [Test]
    public async Task Drops_stale_position()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"tour-guide-stale-{Guid.NewGuid():N}")
            .Options;

        await using var context = new ApplicationDbContext(options);

        var route = new Route { Id = Guid.NewGuid(), RouteName = "Tuyến 2", RouteCode = "WB-2" };
        var tripId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        context.Routes.Add(route);
        context.Trips.Add(new Trip
        {
            Id = tripId,
            TripCode = "BB-20260811-WB-2-0900",
            RouteId = route.Id,
            Route = route,
            DepartureTime = now,
            ArrivalTime = now.AddMinutes(30),
        });
        context.BoatLatestLocations.Add(new BoatLatestLocation
        {
            BoatId = Guid.NewGuid(),
            GpsDeviceId = Guid.NewGuid(),
            TripId = tripId,
            Latitude = 10.7m,
            Longitude = 106.7m,
            RecordedAt = now.AddMinutes(-30), // quá 5 phút
            Sequence = 1,
        });
        await context.SaveChangesAsync();

        var result = await new TourGuideContextReader(context, new FakeTime(now))
            .ReadTripAsync(tripId, CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Position, Is.Null);
        Assert.That(result.PromptBlock, Does.Contain("Chưa có tín hiệu GPS"));
    }

    [Test]
    public async Task Returns_null_for_unknown_trip()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"tour-guide-missing-{Guid.NewGuid():N}")
            .Options;

        await using var context = new ApplicationDbContext(options);

        var result = await new TourGuideContextReader(context, new FakeTime(DateTimeOffset.UtcNow))
            .ReadTripAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
