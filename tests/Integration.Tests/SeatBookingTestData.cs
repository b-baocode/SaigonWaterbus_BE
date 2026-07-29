using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;

namespace SaigonWaterbus.Integration.Tests;

internal sealed record SeededTrip(Guid TripId, string TripCode, Guid TripSeatId);

/// <summary>
/// Seed tối thiểu để đặt được vé trên Postgres thật: tuyến 3 bến BB → HB → LT, tàu 1 ghế
/// STANDARD, chuyến Regular khởi hành sau <paramref name="hoursFromNow"/> giờ.
/// </summary>
internal static class SeatBookingTestData
{
    /// <summary>roles.role_code là UNIQUE trên DB thật nên phải dùng lại role, không tạo mới mỗi user.</summary>
    public static async Task<Guid> SeedCustomerAsync(ApplicationDbContext context, string suffix)
    {
        var role = await context.Set<Role>()
            .FirstOrDefaultAsync(x => x.Code == Roles.CustomerCode, CancellationToken.None);
        if (role is null)
        {
            role = new Role
            {
                Code = Roles.CustomerCode,
                SystemName = Roles.CustomerSystemName,
                DisplayName = "Customer"
            };
            context.Add(role);
        }

        var user = new User
        {
            FullName = $"Khach {suffix}",
            Email = $"khach-{suffix}@example.test",
            RoleId = role.Id,
            Role = role,
            Status = UserStatus.Active
        };

        context.Add(user);
        await context.SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }

    public static async Task<SeededTrip> SeedTripAsync(
        ApplicationDbContext context,
        DateTimeOffset now,
        string tripCode,
        double hoursFromNow = 5)
    {
        var stations = new[]
        {
            new Station { StationCode = FromStationCode(tripCode), StationName = "Bach Dang" },
            new Station { StationCode = MidStationCode(tripCode), StationName = "Hiep Binh" },
            new Station { StationCode = ToStationCode(tripCode), StationName = "Linh Trung" }
        };

        var route = new Route
        {
            RouteCode = $"R-{tripCode}",
            RouteName = "BB - LT",
            RouteType = RouteTypes.Regular,
            IsBookable = true
        };
        for (var i = 0; i < stations.Length; i++)
        {
            route.RouteStops.Add(new RouteStop
            {
                Route = route,
                Station = stations[i],
                StationId = stations[i].Id,
                StopOrder = i + 1,
                DistanceFromPreviousKm = i == 0 ? null : 3m
            });
        }

        var boat = new Boat
        {
            Code = $"V{Guid.NewGuid():N}"[..18],
            Name = "Integration boat",
            Status = BoatStatus.Active,
            SeatCount = 1,
            NumberOfDecks = 1,
            SeatSetupType = SeatSetupType.FullStandard,
            SeatsConfigured = true
        };
        var seat = new Seat { Boat = boat, BoatId = boat.Id, Code = "A1", Deck = 1, Row = "A", Column = 1 };

        var departure = now.AddHours(hoursFromNow);
        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            Boat = boat,
            BoatId = boat.Id,
            TripCode = tripCode,
            TripType = TripTypes.Regular,
            OperatingDate = DateOnly.FromDateTime(departure.UtcDateTime),
            DepartureTime = departure,
            ArrivalTime = departure.AddMinutes(30),
            CapacitySnapshot = 1,
            TripStatus = TripStatus.Scheduled
        };
        var tripSeat = new TripSeat
        {
            Trip = trip,
            TripId = trip.Id,
            Seat = seat,
            SeatId = seat.Id,
            Price = 10000m
        };

        context.AddRange(stations);
        context.AddRange(route, boat, seat, trip, tripSeat);
        await context.SaveChangesAsync(CancellationToken.None);

        return new SeededTrip(trip.Id, trip.TripCode, tripSeat.Id);
    }

    // Mã bến của tuyến seed — test dùng chung đúng các hàm này để không lệch với seed.
    public static string FromStationCode(string tripCode) => $"BB{tripCode}"[..8];

    public static string MidStationCode(string tripCode) => $"HB{tripCode}"[..8];

    public static string ToStationCode(string tripCode) => $"LT{tripCode}"[..8];
}

internal sealed class TestUserContext(Guid userId) : IUserContext
{
    public Guid? UserId { get; } = userId;

    public bool IsAuthenticated => true;
}

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

internal sealed class GuidBookingCodeGenerator : IBookingCodeGenerator
{
    public Task<string> GenerateAsync(CancellationToken cancellationToken) =>
        Task.FromResult($"BK-IT-{Guid.NewGuid():N}"[..16]);
}

internal sealed class FixedFareCalculator(decimal fare) : IFareCalculator
{
    public Task<decimal> CalculateAsync(
        Guid seatId,
        string ticketTypeCode,
        CancellationToken cancellationToken,
        Guid? tripId = null) =>
        Task.FromResult(fare);
}
