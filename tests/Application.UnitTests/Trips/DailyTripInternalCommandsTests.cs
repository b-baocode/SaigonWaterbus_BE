using FluentValidation.TestHelper;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Redis;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.UnitTests.Trips;

public sealed class DailyTripInternalCommandsTests
{
    private static readonly DateOnly OperatingDate = new(2030, 1, 15);

    [Test]
    public void Clean_validator_requires_explicit_confirmation()
    {
        var result = new CleanTripsCommandValidator()
            .TestValidate(new CleanTripsCommand(OperatingDate, ConfirmDelete: false));

        result.ShouldHaveValidationErrorFor(x => x.ConfirmDelete);
    }

    [Test]
    public void Batch_validator_requires_explicit_confirmation()
    {
        var result = new CreateDailyTripBatchCommandValidator().TestValidate(
            new CreateDailyTripBatchCommand(
                OperatingDate,
                ConfirmCreate: false,
                [Item("WB-BD-LB", "WB_001", new TimeOnly(8, 0))]));

        result.ShouldHaveValidationErrorFor(x => x.ConfirmCreate);
    }

    [Test]
    public void Batch_validator_handles_null_items_without_throwing()
    {
        var result = new CreateDailyTripBatchCommandValidator().TestValidate(
            new CreateDailyTripBatchCommand(OperatingDate, ConfirmCreate: true, null!));

        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Test]
    public void Batch_validator_accepts_plan_with_configurable_interval_and_stops()
    {
        var result = new CreateDailyTripBatchCommandValidator().TestValidate(
            new CreateDailyTripBatchCommand(
                OperatingDate,
                ConfirmCreate: true,
                Plans:
                [
                    new DailyTripBatchPlan(
                        "WB-BD-LB",
                        ["WB_001", "WB_002"],
                        new TimeOnly(14, 35),
                        new TimeOnly(17, 0),
                        IntervalMinutes: 10,
                        Stops: [new CreateTripStopScheduleInput(2, 5)])
                ]));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void Batch_validator_accepts_uniform_stay_duration_for_all_intermediate_stops()
    {
        var result = new CreateDailyTripBatchCommandValidator().TestValidate(
            new CreateDailyTripBatchCommand(
                OperatingDate,
                ConfirmCreate: true,
                Plans:
                [
                    new DailyTripBatchPlan(
                        "WB-BD-LB",
                        ["WB_001", "WB_002"],
                        new TimeOnly(14, 35),
                        new TimeOnly(17, 0),
                        IntervalMinutes: 10,
                        StayDurationMinutes: 5)
                ]));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void Batch_validator_rejects_uniform_and_per_stop_durations_together()
    {
        var result = new CreateDailyTripBatchCommandValidator().TestValidate(
            new CreateDailyTripBatchCommand(
                OperatingDate,
                ConfirmCreate: true,
                Plans:
                [
                    new DailyTripBatchPlan(
                        "WB-BD-LB",
                        ["WB_001"],
                        new TimeOnly(14, 35),
                        new TimeOnly(17, 0),
                        IntervalMinutes: 10,
                        Stops: [new CreateTripStopScheduleInput(2, 3)],
                        StayDurationMinutes: 5)
                ]));

        result.ShouldHaveValidationErrorFor("Plans[0].Stops");
    }

    [Test]
    public async Task Batch_handler_rejects_stop_duration_for_sightseeing_route()
    {
        await using var context = SeatFlowTestData.CreateContext();
        context.Routes.Add(new SaigonWaterbus.Domain.Entities.Route
        {
            RouteCode = "LOOP-BD",
            RouteName = "Sightseeing loop",
            RouteType = RouteTypes.SightseeingLoop,
            Status = "Active",
            IsBookable = true
        });
        await context.SaveChangesAsync();

        var command = new CreateDailyTripBatchCommand(
            OperatingDate,
            ConfirmCreate: true,
            Plans:
            [
                new DailyTripBatchPlan(
                    "LOOP-BD",
                    ["WS_001"],
                    new TimeOnly(14, 35),
                    new TimeOnly(17, 0),
                    IntervalMinutes: 15,
                    StayDurationMinutes: 5)
            ]);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CreateDailyTripBatchCommandHandler(context)
                .Handle(command, CancellationToken.None));

        exception.Errors["stops"].Single()
            .ShouldContain("Sightseeing không có điểm dừng");
    }

    [Test]
    public void Batch_validator_rejects_plan_interval_below_five_minutes()
    {
        var result = new CreateDailyTripBatchCommandValidator().TestValidate(
            new CreateDailyTripBatchCommand(
                OperatingDate,
                ConfirmCreate: true,
                Plans:
                [
                    new DailyTripBatchPlan(
                        "WB-BD-LB",
                        ["WB_001"],
                        new TimeOnly(14, 35),
                        new TimeOnly(17, 0),
                        IntervalMinutes: 4)
                ]));

        result.ShouldHaveValidationErrorFor("Plans[0].IntervalMinutes");
    }

    [Test]
    public void Batch_validator_handles_null_departure_times_without_throwing()
    {
        var result = new CreateDailyTripBatchCommandValidator().TestValidate(
            new CreateDailyTripBatchCommand(
                OperatingDate,
                ConfirmCreate: true,
                [new DailyTripBatchItem("WB-BD-LB", "WB_001", null!)]));

        result.ShouldHaveValidationErrorFor("Items[0].DepartureTimes");
    }

    [Test]
    public void Batch_validator_rejects_same_boat_and_time_across_routes()
    {
        var result = new CreateDailyTripBatchCommandValidator().TestValidate(
            new CreateDailyTripBatchCommand(
                OperatingDate,
                ConfirmCreate: true,
                [
                    Item("WB-BD-LB", "WB_001", new TimeOnly(8, 0)),
                    Item("WB-LB-BD", "wb_001", new TimeOnly(8, 0))
                ]));

        result.ShouldHaveValidationErrorFor(x => x.Items)
            .WithErrorMessage("Một tàu không được có hai trip cùng giờ trong batch.");
    }

    [Test]
    public async Task Clean_handler_deletes_all_trip_types_only_on_requested_date()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var waterbus = Trip("WB-1", OperatingDate, TripTypes.Regular);
        var sightseeing = Trip("SS-1", OperatingDate, TripTypes.Regular);
        var charter = Trip("CH-1", OperatingDate, TripTypes.Charter);
        var anotherDate = Trip("WB-2", OperatingDate.AddDays(1), TripTypes.Regular);

        var deletedSeat = new TripSeat
        {
            TripId = waterbus.Id,
            Trip = waterbus,
            SeatId = Guid.NewGuid()
        };
        var deletedStop = new TripStop
        {
            TripId = sightseeing.Id,
            Trip = sightseeing,
            StationId = Guid.NewGuid(),
            StopOrder = 1
        };
        var retainedSeat = new TripSeat
        {
            TripId = anotherDate.Id,
            Trip = anotherDate,
            SeatId = Guid.NewGuid()
        };

        context.AddRange(waterbus, sightseeing, charter, anotherDate, deletedSeat, deletedStop, retainedSeat);
        await context.SaveChangesAsync();
        var seatHoldService = new InMemorySeatHoldService();
        await seatHoldService.TryHoldAsync(
            waterbus.Id,
            [deletedSeat.Id],
            Guid.NewGuid(),
            1,
            2,
            TimeSpan.FromMinutes(3),
            CancellationToken.None);

        var result = await new CleanTripsCommandHandler(context, seatHoldService).Handle(
            new CleanTripsCommand(OperatingDate, ConfirmDelete: true),
            CancellationToken.None);

        result.Deleted.ShouldBe(3);
        (await context.Trips.Select(x => x.TripCode).ToListAsync()).ShouldBe(["WB-2"]);
        (await context.Set<TripSeat>().Select(x => x.Id).ToListAsync()).ShouldBe([retainedSeat.Id]);
        (await context.Set<TripStop>().CountAsync()).ShouldBe(0);
        (await seatHoldService.GetHeldSeatsAsync(waterbus.Id, CancellationToken.None)).ShouldBeEmpty();
    }

    [Test]
    public async Task Clean_handler_deletes_customer_data_linked_to_requested_date()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var targetTrip = Trip("WB-DEMO", OperatingDate, TripTypes.Regular);
        var retainedTrip = Trip("WB-RETAIN", OperatingDate.AddDays(1), TripTypes.Regular);
        var customer = new User
        {
            FullName = "Demo customer",
            Email = "demo-clean@example.test",
            PointBalance = 900
        };
        var deletedBooking = Booking("BK-DELETED", customer, targetTrip);
        var retainedBooking = Booking("BK-RETAINED", customer, retainedTrip);
        var passenger = new BookingPassenger
        {
            Booking = deletedBooking,
            BookingId = deletedBooking.Id,
            Trip = targetTrip,
            TripId = targetTrip.Id,
            FullName = "Demo passenger"
        };
        var ticket = new Ticket
        {
            Booking = deletedBooking,
            BookingId = deletedBooking.Id,
            BookingPassenger = passenger,
            BookingPassengerId = passenger.Id,
            TicketCode = "TK-DELETED",
            QrToken = "QR-DELETED",
            IssuedAt = DateTimeOffset.UtcNow
        };
        var payment = new Payment
        {
            Booking = deletedBooking,
            BookingId = deletedBooking.Id,
            PaymentCode = "PAY-DELETED",
            Amount = 100_000
        };
        var pointTransaction = new PointTransaction
        {
            User = customer,
            UserId = customer.Id,
            Booking = deletedBooking,
            BookingId = deletedBooking.Id,
            TransactionType = "Redeem",
            Points = -100,
            BalanceAfter = 900,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var scanEvent = new TicketScanEvent
        {
            Ticket = ticket,
            TicketId = ticket.Id,
            Booking = deletedBooking,
            BookingId = deletedBooking.Id,
            Trip = targetTrip,
            TripId = targetTrip.Id,
            PerformedByUser = customer,
            PerformedByUserId = customer.Id,
            Action = TicketScanAction.CheckIn,
            Result = TicketScanResult.Success,
            ServerTime = DateTimeOffset.UtcNow
        };
        var review = new Review
        {
            Customer = customer,
            CustomerId = customer.Id,
            Booking = deletedBooking,
            BookingId = deletedBooking.Id,
            Trip = targetTrip,
            TripId = targetTrip.Id,
            Rating = 5
        };

        context.AddRange(
            targetTrip,
            retainedTrip,
            customer,
            deletedBooking,
            retainedBooking,
            passenger,
            ticket,
            payment,
            pointTransaction,
            scanEvent,
            review);
        await context.SaveChangesAsync();

        var result = await new CleanTripsCommandHandler(context).Handle(
            new CleanTripsCommand(OperatingDate, ConfirmDelete: true),
            CancellationToken.None);

        result.Deleted.ShouldBe(1);
        result.DeletedBookings.ShouldBe(1);
        result.DeletedTickets.ShouldBe(1);
        result.DeletedPayments.ShouldBe(1);
        result.DeletedPassengers.ShouldBe(1);
        (await context.Set<Booking>().Select(x => x.BookingCode).ToListAsync())
            .ShouldBe(["BK-RETAINED"]);
        (await context.Set<BookingPassenger>().CountAsync()).ShouldBe(0);
        (await context.Set<Ticket>().CountAsync()).ShouldBe(0);
        (await context.Set<Payment>().CountAsync()).ShouldBe(0);
        (await context.Set<PointTransaction>().CountAsync()).ShouldBe(0);
        (await context.Set<TicketScanEvent>().CountAsync()).ShouldBe(0);
        (await context.Set<Review>().CountAsync()).ShouldBe(0);
        customer.PointBalance.ShouldBe(1_000);
        (await context.Set<Trip>().Select(x => x.TripCode).ToListAsync())
            .ShouldBe(["WB-RETAIN"]);
    }

    private static DailyTripBatchItem Item(
        string routeCode,
        string boatCode,
        params TimeOnly[] departureTimes) =>
        new(routeCode, boatCode, departureTimes);

    private static Trip Trip(string code, DateOnly operatingDate, string tripType) =>
        new()
        {
            TripCode = code,
            TripType = tripType,
            RouteId = Guid.NewGuid(),
            OperatingDate = operatingDate,
            DepartureTime = new DateTimeOffset(
                operatingDate.ToDateTime(new TimeOnly(8, 0)),
                TimeSpan.FromHours(7)),
            ArrivalTime = new DateTimeOffset(
                operatingDate.ToDateTime(new TimeOnly(9, 0)),
                TimeSpan.FromHours(7)),
            CapacitySnapshot = 50
        };

    private static Booking Booking(string code, User customer, Trip trip) =>
        new()
        {
            BookingCode = code,
            ContactName = customer.FullName,
            User = customer,
            UserId = customer.Id,
            Trip = trip,
            TripId = trip.Id
        };
}
