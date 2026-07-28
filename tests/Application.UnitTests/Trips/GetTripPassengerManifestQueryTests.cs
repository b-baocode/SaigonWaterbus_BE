using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Trips;

public class GetTripPassengerManifestQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 1, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AdminManagerStaffCanViewPassengersOfTrip()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var seeded = SeedTripWithPassenger(context);
        await context.SaveChangesAsync();

        var result = await new GetTripPassengerManifestQueryHandler(
                context,
                admin,
                new FixedTimeProvider(Now))
            .Handle(new GetTripPassengerManifestQuery(seeded.Trip.Id), CancellationToken.None);

        result.TripId.ShouldBe(seeded.Trip.Id);
        result.PassengerCount.ShouldBe(1);
        result.ActiveTicketCount.ShouldBe(1);
        result.CheckedInTicketCount.ShouldBe(0);
        var passenger = result.Passengers.Single();
        passenger.BookingCode.ShouldBe("BK-TRIP-MANIFEST");
        passenger.FullName.ShouldBe("Nguyen Van A");
        passenger.PassengerType.ShouldBe("ADULT");
        passenger.SeatNumber.ShouldBe("A1");
        passenger.FromStationCode.ShouldBe("BD");
        passenger.ToStationCode.ShouldBe("LD");
        passenger.ScheduledBoardingAt.ShouldBe(seeded.Trip.DepartureTime);
        passenger.ScheduledAlightingAt.ShouldBe(seeded.Trip.ArrivalTime);
        passenger.TicketCode.ShouldBe("TK-TRIP-001");
        passenger.TicketStatus.ShouldBe(nameof(TicketStatus.Active));
        passenger.CanCheckIn.ShouldBeTrue();
    }

    [Test]
    public async Task CustomerCannotViewTripPassengerManifest()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customer = await SeatFlowTestData.SeedCustomerAsync(context);
        var seeded = SeedTripWithPassenger(context);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ForbiddenAccessException>(() => new GetTripPassengerManifestQueryHandler(
                context,
                customer,
                new FixedTimeProvider(Now))
            .Handle(new GetTripPassengerManifestQuery(seeded.Trip.Id), CancellationToken.None));
    }

    private sealed record SeededTrip(Trip Trip);

    private static SeededTrip SeedTripWithPassenger(Infrastructure.Data.ApplicationDbContext context)
    {
        var departure = new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();
        var stationA = new Station
        {
            StationCode = "BD",
            StationName = "Bach Dang",
            Status = StationStatus.Active
        };
        var stationB = new Station
        {
            StationCode = "LD",
            StationName = "Linh Dong",
            Status = StationStatus.Active
        };
        var route = new Route
        {
            RouteCode = "R-BD-LD",
            RouteName = "Bach Dang - Linh Dong",
            RouteType = RouteTypes.Regular,
            IsBookable = true,
            Status = "Active"
        };
        route.RouteStops.Add(new RouteStop
        {
            Route = route,
            Station = stationA,
            StationId = stationA.Id,
            StopOrder = 1
        });
        route.RouteStops.Add(new RouteStop
        {
            Route = route,
            Station = stationB,
            StationId = stationB.Id,
            StopOrder = 2,
            StandardTravelMin = 60
        });
        var boat = new Boat
        {
            Code = "BOAT-MANIFEST",
            Name = "Boat Manifest",
            Status = BoatStatus.Active,
            SeatCount = 10,
            NumberOfDecks = 1,
            SeatSetupType = SeatSetupType.FullStandard,
            SeatsConfigured = true
        };
        var seat = new Seat
        {
            Boat = boat,
            BoatId = boat.Id,
            Code = "A1",
            Deck = 1,
            Row = "A",
            Column = 1,
            SeatTypeCode = "STANDARD",
            IsActive = true
        };
        var trip = new Trip
        {
            Boat = boat,
            BoatId = boat.Id,
            Route = route,
            RouteId = route.Id,
            TripCode = "TR-MANIFEST",
            TripType = TripTypes.Regular,
            OperatingDate = DateOnly.FromDateTime(departure.ToOffset(TimeSpan.FromHours(7)).Date),
            DepartureTime = departure,
            ArrivalTime = departure.AddHours(1),
            CapacitySnapshot = 10,
            TripStatus = TripStatus.Scheduled
        };
        trip.TripStops.Add(new TripStop
        {
            Trip = trip,
            Station = stationA,
            StationId = stationA.Id,
            StopOrder = 1,
            PlannedDepartureTime = trip.DepartureTime
        });
        trip.TripStops.Add(new TripStop
        {
            Trip = trip,
            Station = stationB,
            StationId = stationB.Id,
            StopOrder = 2,
            PlannedArrivalTime = trip.ArrivalTime
        });
        var tripSeat = new TripSeat
        {
            Trip = trip,
            TripId = trip.Id,
            Seat = seat,
            SeatId = seat.Id,
            Status = TripSeat.StatusBooked
        };
        var booking = new Booking
        {
            Trip = trip,
            TripId = trip.Id,
            BookingCode = "BK-TRIP-MANIFEST",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            SubtotalAmount = 12000,
            TotalAmount = 12000,
            RemainingAmount = 0
        };
        var passenger = new BookingPassenger
        {
            Booking = booking,
            BookingId = booking.Id,
            Trip = trip,
            TripId = trip.Id,
            TripSeat = tripSeat,
            TripSeatId = tripSeat.Id,
            FromStation = stationA,
            FromStationId = stationA.Id,
            ToStation = stationB,
            ToStationId = stationB.Id,
            FromStopOrder = 1,
            ToStopOrder = 2,
            FullName = "Nguyen Van A",
            PhoneNumber = "0900000000",
            PassengerType = "ADULT",
            UnitPrice = 12000
        };
        var ticket = new Ticket
        {
            Booking = booking,
            BookingId = booking.Id,
            BookingPassenger = passenger,
            BookingPassengerId = passenger.Id,
            TicketCode = "TK-TRIP-001",
            QrToken = "QR-TRIP-001",
            TicketStatus = TicketStatus.Active,
            IssuedAt = Now
        };

        context.AddRange(boat, route, trip, seat, tripSeat, booking, passenger, ticket);
        return new SeededTrip(trip);
    }
}
