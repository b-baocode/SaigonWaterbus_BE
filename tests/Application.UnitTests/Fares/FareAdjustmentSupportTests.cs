using NUnit.Framework;
using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Fares;

public class FareAdjustmentSupportTests
{
    [Test]
    public async Task WeekendAdjustmentAppliesOnlyToWeekendDates()
    {
        await using var context = SeatFlowTestData.CreateContext();
        context.Add(new FareAdjustment
        {
            Scope = FareAdjustmentScopes.Weekend,
            Name = "Weekend",
            SurchargePercent = 20m,
            RoundingStep = 1000m,
            IsActive = true
        });
        await context.SaveChangesAsync();

        var saturday = await FareAdjustmentSupport.GetEffectiveAdjustmentAsync(
            context, new DateOnly(2026, 7, 25), CancellationToken.None);
        var monday = await FareAdjustmentSupport.GetEffectiveAdjustmentAsync(
            context, new DateOnly(2026, 7, 27), CancellationToken.None);

        saturday.ShouldNotBeNull();
        saturday.Scope.ShouldBe(FareAdjustmentScopes.Weekend);
        FareAdjustmentSupport.ApplySurcharge(10_000m, saturday).ShouldBe(12_000m);
        monday.ShouldBeNull();
    }

    [Test]
    public async Task SpecificDateAdjustmentOverridesWeekend()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var date = new DateOnly(2026, 7, 25);
        context.AddRange(
            new FareAdjustment
            {
                Scope = FareAdjustmentScopes.Weekend,
                Name = "Weekend",
                SurchargePercent = 20m,
                RoundingStep = 1000m,
                IsActive = true
            },
            new FareAdjustment
            {
                Scope = FareAdjustmentScopes.Holiday,
                Date = date,
                Name = "Holiday",
                SurchargePercent = 50m,
                RoundingStep = 1000m,
                IsActive = true
            });
        await context.SaveChangesAsync();

        var adjustment = await FareAdjustmentSupport.GetEffectiveAdjustmentAsync(
            context, date, CancellationToken.None);

        adjustment.ShouldNotBeNull();
        adjustment.Scope.ShouldBe(FareAdjustmentScopes.Holiday);
        FareAdjustmentSupport.ApplySurcharge(10_000m, adjustment).ShouldBe(15_000m);
    }

    [Test]
    public async Task FareCalculatorIgnoresTripSeatSnapshotAndAppliesCurrentAdjustment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var seatType = new SeatType
        {
            Code = "CABIN",
            Name = "Cabin",
            BasePrice = 10_000m,
            Currency = "VND",
            IsActive = true
        };
        var boat = new Boat
        {
            Code = "BOAT-FARE",
            Name = "Fare boat",
            Status = BoatStatus.Active,
            SeatSetupType = SeatSetupType.StandardAndVip,
            SeatsConfigured = true
        };
        var seat = new Seat
        {
            Boat = boat,
            BoatId = boat.Id,
            SeatType = seatType,
            SeatTypeId = seatType.Id,
            SeatTypeCode = "CABIN",
            Code = "A1",
            Deck = 1,
            Row = "A",
            Column = 1,
            IsActive = true
        };
        var trip = new Trip
        {
            Boat = boat,
            BoatId = boat.Id,
            TripCode = "TR-FARE",
            TripType = TripTypes.Regular,
            OperatingDate = new DateOnly(2026, 7, 25),
            DepartureTime = new DateTimeOffset(2026, 7, 25, 8, 0, 0, TimeSpan.Zero),
            ArrivalTime = new DateTimeOffset(2026, 7, 25, 9, 0, 0, TimeSpan.Zero),
            CapacitySnapshot = 1,
            TripStatus = TripStatus.Scheduled
        };

        context.AddRange(
            seatType,
            boat,
            seat,
            trip,
            new TripSeat { Trip = trip, TripId = trip.Id, Seat = seat, SeatId = seat.Id, Price = 50_000m },
            new FareAdjustment
            {
                Scope = FareAdjustmentScopes.Weekend,
                Name = "Weekend",
                SurchargePercent = 20m,
                RoundingStep = 1000m,
                IsActive = true
            });
        await context.SaveChangesAsync();

        var price = await new FareCalculator(context).CalculateAsync(
            seat.Id, "ADULT", CancellationToken.None, trip.Id);

        price.ShouldBe(12_000m);
    }
}
