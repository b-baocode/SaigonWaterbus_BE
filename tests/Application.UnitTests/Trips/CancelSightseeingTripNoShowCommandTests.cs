using NUnit.Framework;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.UnitTests.Trips;

public class CancelSightseeingTripNoShowCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 11, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task CancelsSightseeingTripAndActiveTicketsButKeepsPaidBooking()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var trip = await SeedTripAsync(context, "SIG-NO-SHOW-1", RouteTypes.SightseeingLoop);
        var userId = Guid.NewGuid();
        var booking = await SeedConfirmedBookingWithTicketAsync(context, trip, userId, TicketStatus.Active);
        var notifier = new RecordingNotificationRealtimeNotifier();

        var result = await new CancelSightseeingTripNoShowCommandHandler(
                context,
                new FixedTimeProvider(Now),
                notifier)
            .Handle(new CancelSightseeingTripNoShowCommand(trip.Id, "Khách không có mặt tại bến"), CancellationToken.None);

        result.TripStatus.ShouldBe(nameof(TripStatus.Cancelled));
        result.StatusNote.ShouldBe("Khách không có mặt tại bến");
        trip.TripStatus.ShouldBe(TripStatus.Cancelled);
        booking.BookingStatus.ShouldBe(BookingStatus.Confirmed);
        booking.PaymentStatus.ShouldBe(PaymentSupport.PaidBookingPaymentStatus);
        context.Set<Ticket>().Single(x => x.BookingId == booking.Id).TicketStatus.ShouldBe(TicketStatus.Cancelled);

        var notification = context.Set<Notification>().Single();
        notification.UserId.ShouldBe(userId);
        notification.Type.ShouldBe("trip_cancelled");
        notification.Body.ShouldNotBeNull();
        notification.Body.ShouldContain("Khách không có mặt tại bến");
        notifier.Published.Count.ShouldBe(1);
    }

    [Test]
    public async Task RejectsNoShowCancellationWhenAnyTicketWasCheckedIn()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var trip = await SeedTripAsync(context, "SIG-NO-SHOW-2", RouteTypes.SightseeingLoop);
        await SeedConfirmedBookingWithTicketAsync(context, trip, Guid.NewGuid(), TicketStatus.CheckedIn);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CancelSightseeingTripNoShowCommandHandler(context, new FixedTimeProvider(Now))
                .Handle(new CancelSightseeingTripNoShowCommand(trip.Id), CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(m => m.Contains("đã có vé check-in/check-out"));
        trip.TripStatus.ShouldBe(TripStatus.Scheduled);
    }

    [Test]
    public async Task RejectsNoShowCancellationForRegularTrip()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var trip = await SeedTripAsync(context, "REG-NO-SHOW-1", RouteTypes.Regular);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CancelSightseeingTripNoShowCommandHandler(context, new FixedTimeProvider(Now))
                .Handle(new CancelSightseeingTripNoShowCommand(trip.Id), CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(m => m.Contains("Chỉ chuyến sightseeing"));
        trip.TripStatus.ShouldBe(TripStatus.Scheduled);
    }

    [Test]
    public async Task RejectsNoShowCancellationWhenBoatAlreadyDepartedEvenIfTripStatusWasNotUpdated()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var trip = await SeedTripAsync(context, "SIG-NO-SHOW-3", RouteTypes.SightseeingLoop);
        var station = new Station
        {
            StationCode = "BD",
            StationName = "Bến Bạch Đằng",
            Status = StationStatus.Active
        };
        context.Set<TripStop>().Add(new TripStop
        {
            Trip = trip,
            TripId = trip.Id,
            Station = station,
            StationId = station.Id,
            StopOrder = 1,
            PlannedDepartureTime = Now.AddMinutes(-5),
            ActualDepartureTime = Now,
            StopStatus = TripStopStatuses.Departed
        });
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CancelSightseeingTripNoShowCommandHandler(context, new FixedTimeProvider(Now))
                .Handle(new CancelSightseeingTripNoShowCommand(trip.Id), CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(m => m.Contains("Tàu đã rời bến"));
        trip.TripStatus.ShouldBe(TripStatus.Scheduled);
    }

    private static async Task<Trip> SeedTripAsync(
        Infrastructure.Data.ApplicationDbContext context,
        string tripCode,
        string routeType)
    {
        var route = new Route
        {
            RouteCode = $"R-{tripCode}",
            RouteName = $"Route {tripCode}",
            RouteType = routeType,
            IsBookable = true
        };
        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            TripCode = tripCode,
            TripType = TripTypes.Regular,
            OperatingDate = DateOnly.FromDateTime(Now.UtcDateTime),
            DepartureTime = Now.AddMinutes(5),
            ArrivalTime = Now.AddHours(1),
            CapacitySnapshot = 10,
            TripStatus = TripStatus.Scheduled
        };
        context.AddRange(route, trip);
        await context.SaveChangesAsync();
        return trip;
    }

    private static async Task<Booking> SeedConfirmedBookingWithTicketAsync(
        Infrastructure.Data.ApplicationDbContext context,
        Trip trip,
        Guid userId,
        TicketStatus ticketStatus)
    {
        var booking = new Booking
        {
            BookingType = Booking.SeatBookingType,
            UserId = userId,
            Trip = trip,
            TripId = trip.Id,
            BookingCode = $"BK-{trip.TripCode}",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = PaymentSupport.PaidBookingPaymentStatus,
            SubtotalAmount = 150000m,
            TotalAmount = 150000m,
            RemainingAmount = 0m
        };
        var passenger = new BookingPassenger
        {
            Booking = booking,
            Trip = trip,
            TripId = trip.Id,
            FullName = "Nguyen Van A",
            PassengerType = "ADULT",
            UnitPrice = 150000m
        };
        var ticket = new Ticket
        {
            Booking = booking,
            BookingPassenger = passenger,
            TicketCode = $"TK-{trip.TripCode}",
            QrToken = $"QR-{trip.TripCode}",
            TicketStatus = ticketStatus,
            IssuedAt = Now,
            CheckedInAt = ticketStatus is TicketStatus.CheckedIn or TicketStatus.CheckedOut ? Now : null
        };

        context.AddRange(booking, passenger, ticket);
        await context.SaveChangesAsync();
        return booking;
    }
}
