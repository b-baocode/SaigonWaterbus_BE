using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data;

///
public static class WaterbusSeedData
{
    public static async Task SeedAsync(ApplicationDbContext context)
    {
        if (await context.Set<Station>().AnyAsync() && await context.Set<Trip>().AnyAsync())
            return;

        var sBD = S("BD", "Bến Bạch Đằng", "Bến Bạch Đằng, Quận 1, TP.HCM", 10.776760m, 106.703500m);
        var sTT = S("TT", "Bến Thủ Thiêm", "Thủ Thiêm, TP. Thủ Đức, TP.HCM", 10.788100m, 106.719500m);
        var sBS = S("BS", "Bến Ba Sơn", "Ba Sơn, Quận 1, TP.HCM", 10.793000m, 106.708300m);
        var sBA = S("BA", "Bến Bình An", "Bình An, TP. Thủ Đức, TP.HCM", 10.803800m, 106.732700m);
        var sTD = S("TD", "Bến Thảo Điền", "Thảo Điền, TP. Thủ Đức, TP.HCM", 10.807700m, 106.738300m);
        var sTADA = S("TADA", "Bến Thanh Đa", "Thanh Đa, Bình Thạnh, TP.HCM", 10.825200m, 106.723000m);
        var sHBC = S("HBC", "Bến Hiệp Bình Chánh", "Hiệp Bình Chánh, TP. Thủ Đức, TP.HCM", 10.832000m, 106.710000m);
        var sLD = S("LD", "Bến Linh Đông", "Linh Đông, TP. Thủ Đức, TP.HCM", 10.841800m, 106.716700m);

        context.Set<Station>().AddRange(sBD, sTT, sBS, sBA, sTD, sTADA, sHBC, sLD);
        await context.SaveChangesAsync();

        var rFwd = new Route
        {
            RouteCode = "R01-BD-LD",
            RouteName = "Tuyến 01: Bạch Đằng → Linh Đông",
            Description = "Tuyến waterbus từ Bến Bạch Đằng (Q.1) đến Linh Đông (TP.Thủ Đức) qua 8 bến.",
            BaseDistanceKm = 10.8m,
            EstimatedDurationMin = 60,
            Status = "Active"
        };
        var rBwd = new Route
        {
            RouteCode = "R01-LD-BD",
            RouteName = "Tuyến 01: Linh Đông → Bạch Đằng",
            Description = "Tuyến waterbus từ Linh Đông (TP.Thủ Đức) đến Bến Bạch Đằng (Q.1) qua 8 bến.",
            BaseDistanceKm = 10.8m,
            EstimatedDurationMin = 60,
            Status = "Active"
        };

        context.Set<Route>().AddRange(rFwd, rBwd);
        await context.SaveChangesAsync();

        var rsF = new[]
        {
            RS(rFwd, sBD,   1, travelMin:  0, dwellMin: 2),
            RS(rFwd, sTT,   2, travelMin:  5, dwellMin: 2),
            RS(rFwd, sBS,   3, travelMin:  5, dwellMin: 2),
            RS(rFwd, sBA,   4, travelMin: 12, dwellMin: 2),
            RS(rFwd, sTD,   5, travelMin:  5, dwellMin: 2),
            RS(rFwd, sTADA, 6, travelMin: 13, dwellMin: 2),
            RS(rFwd, sHBC,  7, travelMin: 10, dwellMin: 2),
            RS(rFwd, sLD,   8, travelMin: 10, dwellMin: 0, pickup: false),
        };

        var rsB = new[]
        {
            RS(rBwd, sLD,   1, travelMin:  0, dwellMin: 2),
            RS(rBwd, sHBC,  2, travelMin: 10, dwellMin: 2),
            RS(rBwd, sTADA, 3, travelMin: 10, dwellMin: 2),
            RS(rBwd, sTD,   4, travelMin: 13, dwellMin: 2),
            RS(rBwd, sBA,   5, travelMin:  5, dwellMin: 2),
            RS(rBwd, sBS,   6, travelMin: 12, dwellMin: 2),
            RS(rBwd, sTT,   7, travelMin:  5, dwellMin: 2),
            RS(rBwd, sBD,   8, travelMin:  5, dwellMin: 0, pickup: false),
        };

        context.Set<RouteStop>().AddRange(rsF);
        context.Set<RouteStop>().AddRange(rsB);
        await context.SaveChangesAsync();

        const int b1 = 80, b2 = 80, b3 = 60;

        var promos = new[]
        {
            new Promotion
            {
                PromotionCode = "WELCOME10", PromotionName = "Chào mừng khách mới", PromotionType = PromotionType.Percent,
                DiscountValue = 10m, MinOrderValue = 15_000m,
                ValidFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime(),
                ValidTo   = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.FromHours(7)).ToUniversalTime(),
                UsageLimit = 1000, UsageCount = 0, Status = "Active"
            },
            new Promotion
            {
                PromotionCode = "SUMMER2026", PromotionName = "Ưu đãi hè 2026", PromotionType = PromotionType.Fixed,
                DiscountValue = 5_000m, MinOrderValue = 20_000m,
                ValidFrom = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime(),
                ValidTo   = new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.FromHours(7)).ToUniversalTime(),
                UsageLimit = 500, UsageCount = 0, Status = "Active"
            },
        };

        context.Set<Promotion>().AddRange(promos);
        await context.SaveChangesAsync();

        var refDate = new DateOnly(2026, 6, 1);
        var vn = TimeSpan.FromHours(7);

        DateTimeOffset T(int h, int m) =>
            new DateTimeOffset(refDate.Year, refDate.Month, refDate.Day, h, m, 0, vn).ToUniversalTime();

        var fwdFull = new[]
        {
            ("D1",  b1, new[]{T( 8,30),T( 8,35),T( 8,40),T( 8,52),T( 8,57),T( 9,10),T( 9,20),T( 9,30)}),
            ("S1",  b3, new[]{T( 9,30),T( 9,35),T( 9,40),T( 9,52),T( 9,57),T(10,10),T(10,20),T(10,30)}),
            ("D7",  b2, new[]{T(11, 0),T(11, 5),T(11,10),T(11,22),T(11,27),T(11,40),T(11,50),T(12, 0)}),
        };

        var fwdShort = new[]
        {
            ("D3",  b2, new[]{T( 9, 0),T( 9, 5),T( 9,10),T( 9,22),T( 9,27)}),
            ("D5",  b1, new[]{T(10,30),T(10,35),T(10,40),T(10,52),T(10,57)}),
            ("S3",  b3, new[]{T(13, 0),T(13, 5),T(13,10),T(13,22),T(13,27)}),
            ("D9",  b1, new[]{T(13,30),T(13,35),T(13,40),T(13,52),T(13,57)}),
            ("D11", b2, new[]{T(14,30),T(14,35),T(14,40),T(14,52),T(14,57)}),
            ("S5",  b3, new[]{T(15, 0),T(15, 5),T(15,10),T(15,22),T(15,27)}),
            ("D13", b1, new[]{T(15,30),T(15,35),T(15,40),T(15,52),T(15,57)}),
            ("D15", b2, new[]{T(16,30),T(16,35),T(16,40),T(16,52),T(16,57)}),
            ("D17", b1, new[]{T(17, 0),T(17, 5),T(17,10),T(17,22),T(17,27)}),
            ("D19", b2, new[]{T(17,10),T(17,15),T(17,20),T(17,32),T(17,37)}),
            ("D21", b1, new[]{T(18,10),T(18,15),T(18,20),T(18,32),T(18,37)}),
            ("D23", b2, new[]{T(18,45),T(18,50),T(18,55),T(19, 7),T(19,12)}),
            ("D25", b1, new[]{T(19,30),T(19,35),T(19,40),T(19,52),T(19,57)}),
            ("S7",  b3, new[]{T(19,45),T(19,50),T(19,55),T(20, 7),T(20,12)}),
            ("D27", b2, new[]{T(20,15),T(20,20),T(20,25),T(20,37),T(20,42)}),
            ("D29", b1, new[]{T(21, 0),T(21, 5),T(21,10),T(21,22),T(21,27)}),
            ("S9",  b3, new[]{T(21,15),T(21,20),T(21,25),T(21,37),T(21,42)}),
            ("D31", b2, new[]{T(21,40),T(21,45),T(21,50),T(22, 2),T(22, 7)}),
            ("D33", b1, new[]{T(22,30),T(22,35),T(22,40),T(22,52),T(22,57)}),
        };

        var tripsToAdd = new List<Trip>();

        foreach (var (code, cap, times) in fwdFull)
        {
            var trip = new Trip
            {
                Route = rFwd,
                TripCode = code,
                OperatingDate = refDate,
                DepartureTime = times[0],
                ArrivalTime = times[^1],
                CapacitySnapshot = cap,
                TripStatus = TripStatus.Scheduled
            };
            tripsToAdd.Add(trip);
        }

        foreach (var (code, cap, times) in fwdShort)
        {
            var trip = new Trip
            {
                Route = rFwd,
                TripCode = code,
                OperatingDate = refDate,
                DepartureTime = times[0],
                ArrivalTime = times[^1],
                CapacitySnapshot = cap,
                TripStatus = TripStatus.Scheduled
            };
            tripsToAdd.Add(trip);
        }

        context.Set<Trip>().AddRange(tripsToAdd);
        await context.SaveChangesAsync();
        tripsToAdd.Clear();

        var bwdFull = new[]
        {
            ("D4",  b1, new[]{T( 9,45),T( 9,55),T(10, 5),T(10,18),T(10,23),T(10,35),T(10,40),T(10,45)}),
            ("D8",  b2, new[]{T(11, 0),T(11,10),T(11,20),T(11,33),T(11,38),T(11,50),T(11,55),T(12, 0)}),
            ("S6",  b3, new[]{T(16, 0),T(16,10),T(16,20),T(16,33),T(16,38),T(16,50),T(16,55),T(17, 0)}),
        };

        var bwdShortFromTD = new[]
        {
            ("D2",  b2, new[]{T( 9,40),T( 9,45),T( 9,57),T(10, 2),T(10, 7)}),
            ("D6",  b1, new[]{T(10, 5),T(10,10),T(10,22),T(10,27),T(10,32)}),
            ("S4",  b3, new[]{T(14, 0),T(14, 5),T(14,17),T(14,22),T(14,27)}),
            ("D10", b2, new[]{T(14,30),T(14,35),T(14,47),T(14,52),T(14,57)}),
            ("D12", b1, new[]{T(16,33),T(16,38),T(16,50),T(16,55),T(17, 0)}),
            ("D14", b2, new[]{T(17,15),T(17,20),T(17,32),T(17,37),T(17,42)}),
            ("D16", b1, new[]{T(18, 0),T(18, 5),T(18,17),T(18,22),T(18,27)}),
            ("D18", b2, new[]{T(18,58),T(19, 3),T(19,15),T(19,20),T(19,25)}),
            ("D20", b1, new[]{T(19,30),T(19,35),T(19,47),T(19,52),T(19,57)}),
            ("S8",  b3, new[]{T(19,30),T(19,35),T(19,47),T(19,52),T(19,57)}),
            ("D22", b2, new[]{T(20,15),T(20,20),T(20,32),T(20,37),T(20,42)}),
            ("D24", b1, new[]{T(20,30),T(20,35),T(20,47),T(20,52),T(20,57)}),
            ("D26", b2, new[]{T(21, 0),T(21, 5),T(21,17),T(21,22),T(21,27)}),
            ("D28", b1, new[]{T(21,45),T(21,50),T(22, 2),T(22, 7),T(22,12)}),
            ("D30", b2, new[]{T(22, 0),T(22, 5),T(22,17),T(22,22),T(22,27)}),
            ("D32", b1, new[]{T(22,20),T(22,25),T(22,37),T(22,42),T(22,47)}),
        };

        var bwdShortFromBA = new[]
        {
            ("S2", b3, new[]{T(8,30),T(8,42),T(8,47),T(8,52)}),
        };

        foreach (var (code, cap, times) in bwdFull)
        {
            var trip = new Trip
            {
                Route = rBwd,
                TripCode = code,
                OperatingDate = refDate,
                DepartureTime = times[0],
                ArrivalTime = times[^1],
                CapacitySnapshot = cap,
                TripStatus = TripStatus.Scheduled
            };
            tripsToAdd.Add(trip);
        }

        foreach (var (code, cap, times) in bwdShortFromTD)
        {
            var trip = new Trip
            {
                Route = rBwd,
                TripCode = code,
                OperatingDate = refDate,
                DepartureTime = times[0],
                ArrivalTime = times[^1],
                CapacitySnapshot = cap,
                TripStatus = TripStatus.Scheduled
            };
            tripsToAdd.Add(trip);
        }

        foreach (var (code, cap, times) in bwdShortFromBA)
        {
            var trip = new Trip
            {
                Route = rBwd,
                TripCode = code,
                OperatingDate = refDate,
                DepartureTime = times[0],
                ArrivalTime = times[^1],
                CapacitySnapshot = cap,
                TripStatus = TripStatus.Scheduled
            };
            tripsToAdd.Add(trip);
        }

        context.Set<Trip>().AddRange(tripsToAdd);
        await context.SaveChangesAsync();
    }


    private static Station S(string code, string name, string? address, decimal lat, decimal lng) => new()
    {
        StationCode = code,
        StationName = name,
        Address = address,
        Latitude = lat,
        Longitude = lng,
        Status = StationStatus.Active
    };

    private static RouteStop RS(Route route, Station station, int order,
        int travelMin, int dwellMin, bool pickup = true) => new()
        {
            Route = route,
            Station = station,
            StopOrder = order,
            StandardTravelMin = travelMin,
            StandardDwellMin = dwellMin,
            IsPickupAllowed = pickup,
            IsDropoffAllowed = true
        };

}
