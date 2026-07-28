using NUnit.Framework;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.UnitTests.Bookings;

public class CreateRoundTripBookingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 4, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task RoundTripBookingCreatesPassengersOnBothTripsAndNotifiesBothTrips()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var outbound = await SeedTripAsync(context, "TR-OUT", "BD", "TADA", Now.AddHours(2));
        var inbound = await SeedTripAsync(context, "TR-RET", "TADA", "BD", Now.AddHours(6));
        var notifier = new RecordingTripSeatNotifier();
        var handler = CreateHandler(context, userContext, notifier);

        var result = await handler.Handle(
            new CreateBookingCommand(
                "TR-OUT",
                [Adult("A1", "BD", "TADA")],
                null,
                "TR-RET",
                [Adult("A1", "TADA", "BD"), Adult("A2", "TADA", "BD")]),
            CancellationToken.None);

        var booking = context.Set<Booking>().Single(x => x.Id == result.BookingId);
        booking.TripId.ShouldBe(outbound.Trip.Id);
        booking.ReturnTripId.ShouldBe(inbound.Trip.Id);
        result.ReturnTripCode.ShouldBe("TR-RET");
        result.ItemCount.ShouldBe(3);

        // Tổng tiền = cộng 2 chiều, không giảm giá khứ hồi; fare default không làm tròn lên nghìn.
        result.SubtotalAmount.ShouldBe(28500m);
        result.TotalAmount.ShouldBe(28500m);

        var passengers = context.Set<BookingPassenger>().Where(p => p.BookingId == booking.Id).ToList();
        passengers.Count(p => p.TripId == outbound.Trip.Id).ShouldBe(1);
        passengers.Count(p => p.TripId == inbound.Trip.Id).ShouldBe(2);

        // Ghế của mỗi chiều thuộc đúng trip đó.
        foreach (var passenger in passengers)
        {
            var tripSeat = context.Set<TripSeat>().Single(ts => ts.Id == passenger.TripSeatId!.Value);
            tripSeat.TripId.ShouldBe(passenger.TripId!.Value);
        }

        // SignalR phát trạng thái Booked cho cả 2 trip.
        notifier.Published.Select(x => x.TripId).ShouldBe(
            [outbound.Trip.Id, inbound.Trip.Id], ignoreOrder: true);
    }

    [Test]
    public async Task ReturnTripCodeWithoutReturnItemsIsRejected()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedTripAsync(context, "TR-OUT", "BD", "TADA", Now.AddHours(2));
        await SeedTripAsync(context, "TR-RET", "TADA", "BD", Now.AddHours(6));
        var handler = CreateHandler(context, userContext);

        await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new CreateBookingCommand("TR-OUT", [Adult("A1", "BD", "TADA")], null, "TR-RET", []),
            CancellationToken.None));
    }

    [Test]
    public async Task ReturnItemsWithoutReturnTripCodeIsRejected()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedTripAsync(context, "TR-OUT", "BD", "TADA", Now.AddHours(2));
        var handler = CreateHandler(context, userContext);

        await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new CreateBookingCommand(
                "TR-OUT", [Adult("A1", "BD", "TADA")], null, null, [Adult("A2", "BD", "TADA")]),
            CancellationToken.None));
    }

    [Test]
    public async Task ReturnLegDepartedTripIsRejected()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedTripAsync(context, "TR-OUT", "BD", "TADA", Now.AddHours(2));
        await SeedTripAsync(context, "TR-RET", "TADA", "BD", Now.AddHours(-1));
        var handler = CreateHandler(context, userContext);

        await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new CreateBookingCommand(
                "TR-OUT", [Adult("A1", "BD", "TADA")], null, "TR-RET", [Adult("A1", "TADA", "BD")]),
            CancellationToken.None));
    }

    [Test]
    public async Task ReturnLegOccupiedSeatIsRejected()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedTripAsync(context, "TR-OUT", "BD", "TADA", Now.AddHours(2));
        var inbound = await SeedTripAsync(context, "TR-RET", "TADA", "BD", Now.AddHours(6));

        // Ghế A1 chiều về đã có người giữ (booking khác còn hiệu lực).
        var otherBooking = new Booking
        {
            UserId = Guid.NewGuid(),
            TripId = inbound.Trip.Id,
            BookingCode = "BK-OTHER",
            ContactName = "Other",
            ContactPhone = "0900000001",
            BookingStatus = BookingStatus.Confirmed
        };
        context.Add(otherBooking);
        context.Add(new BookingPassenger
        {
            Booking = otherBooking,
            FullName = "Other Passenger",
            PassengerType = "ADULT",
            TripId = inbound.Trip.Id,
            TripSeatId = inbound.TripSeatsBySeatCode["A1"].Id
        });
        await context.SaveChangesAsync();

        var handler = CreateHandler(context, userContext);

        var exception = await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new CreateBookingCommand(
                "TR-OUT", [Adult("A1", "BD", "TADA")], null, "TR-RET", [Adult("A1", "TADA", "BD")]),
            CancellationToken.None));
        exception.Errors.SelectMany(x => x.Value).ShouldContain(m => m.Contains("already booked"));
    }

    [Test]
    public async Task SameTripBothLegsWithOverlappingSeatIsRejected()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedTripAsync(context, "TR-OUT", "BD", "TADA", Now.AddHours(2));
        var handler = CreateHandler(context, userContext);

        await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new CreateBookingCommand(
                "TR-OUT", [Adult("A1", "BD", "TADA")], null, "TR-OUT", [Adult("A1", "BD", "TADA")]),
            CancellationToken.None));
    }

    [Test]
    public async Task ExpiredRoundTripBookingReleasesSeatsOnBothTrips()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var outbound = await SeedTripAsync(context, "TR-OUT", "BD", "TADA", Now.AddHours(2));
        var inbound = await SeedTripAsync(context, "TR-RET", "TADA", "BD", Now.AddHours(6));
        var handler = CreateHandler(context, userContext);

        await handler.Handle(
            new CreateBookingCommand(
                "TR-OUT", [Adult("A1", "BD", "TADA")], null, "TR-RET", [Adult("A2", "TADA", "BD")]),
            CancellationToken.None);

        var notifier = new RecordingTripSeatNotifier();
        var expired = await BookingHoldExpirySupport.ExpireOverdueBookingsAsync(
            context,
            notifier,
            Now.AddMinutes(30),
            CancellationToken.None);

        expired.ShouldBe(1);
        notifier.Published.Select(x => x.TripId).ShouldBe(
            [outbound.Trip.Id, inbound.Trip.Id], ignoreOrder: true);
        notifier.Published.SelectMany(x => x.Changes).ShouldAllBe(c => c.Status == "Available");
    }

    [Test]
    public async Task SearchTripsCountsReturnLegSeatsAgainstReturnTrip()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var outbound = await SeedTripAsync(context, "TR-OUT", "BD", "TADA", Now.AddHours(2));
        var inbound = await SeedTripAsync(context, "TR-RET", "TADA", "BD", Now.AddHours(6));
        var handler = CreateHandler(context, userContext);

        await handler.Handle(
            new CreateBookingCommand(
                "TR-OUT",
                [Adult("A1", "BD", "TADA")],
                null,
                "TR-RET",
                [Adult("A1", "TADA", "BD"), Adult("A2", "TADA", "BD")]),
            CancellationToken.None);

        var searchHandler = new SearchTripsQueryHandler(context, new FixedTimeProvider(Now));

        var outboundResults = await searchHandler.Handle(
            new SearchTripsQuery(
                outbound.FromStation.Id, outbound.ToStation.Id, DateOnly.FromDateTime(Now.UtcDateTime)),
            CancellationToken.None);
        var inboundResults = await searchHandler.Handle(
            new SearchTripsQuery(
                inbound.FromStation.Id, inbound.ToStation.Id, DateOnly.FromDateTime(Now.UtcDateTime)),
            CancellationToken.None);

        // Chiều đi bán 1 ghế, chiều về bán 2 ghế — mỗi chiều trừ đúng số ghế của mình (2 ghế/tàu).
        outboundResults.Single(x => x.TripCode == "TR-OUT").AvailableSeats.ShouldBe(1);
        inboundResults.Single(x => x.TripCode == "TR-RET").AvailableSeats.ShouldBe(0);
    }

    [Test]
    public async Task FreeRegularBookingConfirmsAndIssuesTicketOnCreate()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedTripAsync(context, "TR-FREE", "BD", "TADA", Now.AddHours(2));
        var handler = CreateHandler(context, userContext);

        var result = await handler.Handle(
            new CreateBookingCommand(
                "TR-FREE",
                [Adult("A1", "BD", "TADA") with { TicketTypeCode = "SENIOR" }],
                null),
            CancellationToken.None);

        result.TotalAmount.ShouldBe(0m);
        result.BookingStatus.ShouldBe(nameof(BookingStatus.Confirmed));

        var booking = context.Set<Booking>().Single(x => x.Id == result.BookingId);
        booking.PaymentStatus.ShouldBe("Paid");
        booking.RemainingAmount.ShouldBe(0m);

        var payment = context.Set<Payment>().Single(x => x.BookingId == booking.Id);
        payment.Provider.ShouldBe("System");
        payment.PaymentMethod.ShouldBe("Free");
        payment.PaymentStatus.ShouldBe("Paid");
        payment.PaidAt.ShouldNotBeNull();

        var ticket = context.Set<Ticket>().Single(x => x.BookingId == booking.Id);
        ticket.TicketStatus.ShouldBe(TicketStatus.Active);
        ticket.TicketCode.ShouldNotBeNullOrWhiteSpace();
        ticket.QrToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task RoundTripBookingUsesPassengerInsurancePackagePerPassenger()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedTripAsync(context, "TR-INS-OUT", "BD", "TADA", Now.AddHours(2));
        await SeedTripAsync(context, "TR-INS-RET", "TADA", "BD", Now.AddHours(6));
        var insurancePackage = new InsurancePackage
        {
            Code = "PASSENGER_BASIC",
            Name = "Bao hiem hanh khach",
            BookingType = "PassengerInsurance",
            UnitPremiumAmount = 3_000m,
            CoverageAmount = 50_000_000m,
            Currency = "VND",
            IsActive = true,
            DisplayOrder = 1
        };
        context.Add(insurancePackage);
        await context.SaveChangesAsync();

        var result = await CreateHandler(context, userContext).Handle(
            new CreateBookingCommand(
                "TR-INS-OUT",
                [Adult("A1", "BD", "TADA")],
                null,
                "TR-INS-RET",
                [Adult("A1", "TADA", "BD")],
                InsuranceSelected: true),
            CancellationToken.None);

        result.Insurance.ShouldNotBeNull();
        result.Insurance.InsurancePackageId.ShouldBe(insurancePackage.Id);
        result.Insurance.Quantity.ShouldBe(2);
        result.Insurance.TotalAmount.ShouldBe(6_000m);
        result.SubtotalAmount.ShouldBe(25_000m);
        result.TotalAmount.ShouldBe(25_000m);
    }

    private static CreateBookingCommandHandler CreateHandler(
        ApplicationDbContext context,
        TestUserContext userContext,
        ITripSeatNotifier? notifier = null) =>
        new(
            context,
            userContext,
            new SequentialBookingCodeGenerator(),
            new FixedFareCalculator(10000m),
            new FixedTimeProvider(Now),
            tripSeatNotifier: notifier);

    private static BookingItemRequest Adult(string seat, string from, string to) =>
        new(seat, "ADULT", from, to, "Nguyen Van A", null, null, null, null, null);

    private sealed record SeededTrip(
        Trip Trip,
        Station FromStation,
        Station ToStation,
        IReadOnlyDictionary<string, TripSeat> TripSeatsBySeatCode);

    /// <summary>Seed 1 trip Regular sắp khởi hành: route 2 ga + tàu 2 ghế (A1, A2) + trip seats.</summary>
    private static async Task<SeededTrip> SeedTripAsync(
        ApplicationDbContext context,
        string tripCode,
        string fromStationCode,
        string toStationCode,
        DateTimeOffset departureTime)
    {
        var fromStation = await GetOrCreateStationAsync(context, fromStationCode);
        var toStation = await GetOrCreateStationAsync(context, toStationCode);

        var route = new Route
        {
            RouteCode = $"R-{tripCode}",
            RouteName = $"{fromStationCode} - {toStationCode}",
            RouteType = RouteTypes.Regular,
            IsBookable = true
        };
        route.RouteStops.Add(new RouteStop { Route = route, Station = fromStation, StationId = fromStation.Id, StopOrder = 1 });
        route.RouteStops.Add(new RouteStop
        {
            Route = route,
            Station = toStation,
            StationId = toStation.Id,
            StopOrder = 2,
            DistanceFromPreviousKm = 3m
        });

        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, BoatStatus.Active);
        boat.SeatCount = 2;
        var seatA1 = new Seat { Boat = boat, BoatId = boat.Id, Code = "A1", Deck = 1, Row = "A", Column = 1 };
        var seatA2 = new Seat { Boat = boat, BoatId = boat.Id, Code = "A2", Deck = 1, Row = "A", Column = 2 };

        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            Boat = boat,
            BoatId = boat.Id,
            TripCode = tripCode,
            TripType = TripTypes.Regular,
            OperatingDate = DateOnly.FromDateTime(departureTime.UtcDateTime),
            DepartureTime = departureTime,
            ArrivalTime = departureTime.AddHours(1),
            CapacitySnapshot = 2,
            TripStatus = TripStatus.Scheduled
        };

        var tripSeatA1 = new TripSeat { Trip = trip, TripId = trip.Id, Seat = seatA1, SeatId = seatA1.Id, Price = 10000m };
        var tripSeatA2 = new TripSeat { Trip = trip, TripId = trip.Id, Seat = seatA2, SeatId = seatA2.Id, Price = 10000m };

        context.AddRange(route, boat, seatA1, seatA2, trip, tripSeatA1, tripSeatA2);
        await context.SaveChangesAsync();

        return new SeededTrip(trip, fromStation, toStation, new Dictionary<string, TripSeat>
        {
            ["A1"] = tripSeatA1,
            ["A2"] = tripSeatA2
        });
    }

    private static async Task<Station> GetOrCreateStationAsync(ApplicationDbContext context, string stationCode)
    {
        var existing = context.Set<Station>().SingleOrDefault(s => s.StationCode == stationCode);
        if (existing is not null)
        {
            return existing;
        }

        var station = new Station { StationCode = stationCode, StationName = $"Station {stationCode}" };
        context.Add(station);
        await context.SaveChangesAsync();
        return station;
    }

    private sealed class SequentialBookingCodeGenerator : IBookingCodeGenerator
    {
        private int _next;

        public Task<string> GenerateAsync(CancellationToken cancellationToken) =>
            Task.FromResult($"BK-RT-{Interlocked.Increment(ref _next):D4}");
    }

    private sealed class FixedFareCalculator(decimal fare) : IFareCalculator
    {
        public Task<decimal> CalculateAsync(
            Guid seatId,
            string ticketTypeCode,
            CancellationToken cancellationToken,
            Guid? tripId = null) =>
            Task.FromResult(fare);
    }

    private sealed class RecordingTripSeatNotifier : ITripSeatNotifier
    {
        public List<(Guid TripId, IReadOnlyList<TripSeatStatusChange> Changes)> Published { get; } = [];

        public Task PublishSeatStatusChangedAsync(
            Guid tripId,
            IReadOnlyList<TripSeatStatusChange> changes,
            CancellationToken cancellationToken)
        {
            Published.Add((tripId, changes));
            return Task.CompletedTask;
        }
    }
}
