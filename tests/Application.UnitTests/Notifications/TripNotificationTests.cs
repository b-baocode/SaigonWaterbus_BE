using NUnit.Framework;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Notifications;

public class TripNotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 3, 0, 0, TimeSpan.Zero);

    private static Trip CreateTrip(
        DateTimeOffset departure,
        TripStatus status = TripStatus.Scheduled,
        Guid? sourceBookingId = null)
    {
        var route = new Route
        {
            RouteCode = $"RT-{Guid.NewGuid():N}"[..10],
            RouteName = "Bạch Đằng - Thủ Đức"
        };
        return new Trip
        {
            Route = route,
            TripCode = $"TR-{Guid.NewGuid():N}"[..10],
            OperatingDate = DateOnly.FromDateTime(departure.UtcDateTime),
            DepartureTime = departure,
            ArrivalTime = departure.AddMinutes(45),
            CapacitySnapshot = 50,
            TripStatus = status,
            SourceBookingId = sourceBookingId
        };
    }

    private static Booking CreateBooking(
        Guid? userId,
        BookingStatus status = BookingStatus.Confirmed,
        Guid? tripId = null,
        Guid? returnTripId = null,
        string bookingCode = "BK-TRIP")
    {
        return new Booking
        {
            UserId = userId,
            BookingCode = bookingCode,
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = status,
            PaymentStatus = "Paid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            TripId = tripId,
            ReturnTripId = returnTripId
        };
    }

    [Test]
    public async Task CancelTripNotifiesConfirmedBookingsOnBothLegsOnce()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var trip = CreateTrip(Now.AddHours(5));
        context.Add(trip);
        await context.SaveChangesAsync();

        var outboundUser = Guid.NewGuid();
        var returnUser = Guid.NewGuid();
        var outboundBooking = CreateBooking(outboundUser, tripId: trip.Id, bookingCode: "BK-OUT");
        var returnBooking = CreateBooking(returnUser, returnTripId: trip.Id, bookingCode: "BK-RET");
        var pendingBooking = CreateBooking(Guid.NewGuid(), BookingStatus.PendingPayment, tripId: trip.Id, bookingCode: "BK-PEND");
        var guestBooking = CreateBooking(userId: null, tripId: trip.Id, bookingCode: "BK-GUEST");
        context.AddRange(outboundBooking, returnBooking, pendingBooking, guestBooking);
        await context.SaveChangesAsync();

        var realtimeNotifier = new RecordingNotificationRealtimeNotifier();
        var handler = new UpdateTripStatusCommandHandler(context, new FixedTimeProvider(Now), realtimeNotifier);
        await handler.Handle(
            new UpdateTripStatusCommand(trip.Id, TripStatus.Cancelled, "Thời tiết xấu"),
            CancellationToken.None);

        var notifications = context.Set<Notification>().ToList();
        notifications.Count.ShouldBe(2);
        realtimeNotifier.Published.Count.ShouldBe(2);
        realtimeNotifier.Published.Select(x => x.UserId).ShouldBe(
            [outboundUser, returnUser], ignoreOrder: true);
        notifications.ShouldAllBe(n => n.Type == "trip_cancelled");
        notifications.ShouldAllBe(n => n.RelatedEntityType == "booking");
        notifications.ShouldAllBe(n => !n.IsRead);
        notifications.Select(n => n.UserId).ShouldBe([outboundUser, returnUser], ignoreOrder: true);
        notifications.Select(n => n.RelatedEntityId).ShouldBe(
            [outboundBooking.Id, returnBooking.Id], ignoreOrder: true);
        var outboundNotification = notifications.Single(n => n.UserId == outboundUser);
        outboundNotification.Title.ShouldBe("Chuyến đi bị hủy");
        outboundNotification.Body.ShouldNotBeNull();
        outboundNotification.Body.ShouldContain("BK-OUT");
        outboundNotification.Body.ShouldContain("Thời tiết xấu");

        // Đặt lại đúng trạng thái Cancelled lần nữa → không có transition, không bắn thêm.
        await handler.Handle(
            new UpdateTripStatusCommand(trip.Id, TripStatus.Cancelled, "Thời tiết xấu"),
            CancellationToken.None);
        context.Set<Notification>().Count().ShouldBe(2);
        realtimeNotifier.Published.Count.ShouldBe(2);
    }

    [Test]
    public async Task DelayTripCreatesTripDelayedNotification()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var trip = CreateTrip(Now.AddHours(5));
        context.Add(trip);
        await context.SaveChangesAsync();
        var userId = Guid.NewGuid();
        context.Add(CreateBooking(userId, tripId: trip.Id));
        await context.SaveChangesAsync();

        var handler = new UpdateTripStatusCommandHandler(context, new FixedTimeProvider(Now));
        await handler.Handle(
            new UpdateTripStatusCommand(trip.Id, TripStatus.Delayed, null),
            CancellationToken.None);

        var notification = context.Set<Notification>().Single();
        notification.UserId.ShouldBe(userId);
        notification.Type.ShouldBe("trip_delayed");
        notification.Title.ShouldBe("Chuyến đi bị trễ");
    }

    [Test]
    public async Task NonDisruptiveStatusChangeCreatesNoNotifications()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var trip = CreateTrip(Now.AddHours(1));
        context.Add(trip);
        await context.SaveChangesAsync();
        context.Add(CreateBooking(Guid.NewGuid(), tripId: trip.Id));
        await context.SaveChangesAsync();

        var handler = new UpdateTripStatusCommandHandler(context, new FixedTimeProvider(Now));
        await handler.Handle(
            new UpdateTripStatusCommand(trip.Id, TripStatus.Boarding, null),
            CancellationToken.None);

        context.Set<Notification>().Count().ShouldBe(0);
    }

    [Test]
    public async Task CancelCharterTripNotifiesSourceBookingOwner()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var charterUser = Guid.NewGuid();
        var charterBooking = CreateBooking(charterUser, bookingCode: "CB-SRC");
        context.Add(charterBooking);
        await context.SaveChangesAsync();
        var trip = CreateTrip(Now.AddHours(5), sourceBookingId: charterBooking.Id);
        context.Add(trip);
        await context.SaveChangesAsync();

        var handler = new UpdateTripStatusCommandHandler(context, new FixedTimeProvider(Now));
        await handler.Handle(
            new UpdateTripStatusCommand(trip.Id, TripStatus.Cancelled, null),
            CancellationToken.None);

        var notification = context.Set<Notification>().Single();
        notification.UserId.ShouldBe(charterUser);
        notification.RelatedEntityId.ShouldBe(charterBooking.Id);
    }

    [Test]
    public async Task ReminderCreatesOncePerUserPerTripWithinLeadWindow()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var tripSoon = CreateTrip(Now.AddMinutes(45));
        var tripLater = CreateTrip(Now.AddHours(3));
        context.AddRange(tripSoon, tripLater);
        await context.SaveChangesAsync();

        var roundTripUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        context.AddRange(
            CreateBooking(roundTripUser, tripId: tripSoon.Id, returnTripId: tripLater.Id, bookingCode: "BK-RT"),
            CreateBooking(secondUser, tripId: tripSoon.Id, bookingCode: "BK-2"),
            CreateBooking(Guid.NewGuid(), BookingStatus.PendingPayment, tripId: tripSoon.Id, bookingCode: "BK-P"));
        await context.SaveChangesAsync();

        var realtimeNotifier = new RecordingNotificationRealtimeNotifier();
        var created = await TripReminderSupport.AddDueTripRemindersAsync(
            context, Now, CancellationToken.None, realtimeNotifier);

        created.ShouldBe(2);
        realtimeNotifier.Published.Count.ShouldBe(2);
        var notifications = context.Set<Notification>().ToList();
        notifications.ShouldAllBe(n => n.Type == "trip_reminder");
        notifications.ShouldAllBe(n => n.RelatedEntityType == "trip");
        notifications.ShouldAllBe(n => n.RelatedEntityId == tripSoon.Id);
        notifications.Select(n => n.UserId).ShouldBe([roundTripUser, secondUser], ignoreOrder: true);

        // Quét lại trong cùng cửa sổ → không nhắc trùng.
        (await TripReminderSupport.AddDueTripRemindersAsync(context, Now.AddMinutes(1), CancellationToken.None))
            .ShouldBe(0);
        context.Set<Notification>().Count().ShouldBe(2);

        // Chiều về lọt vào cửa sổ 60 phút → nhắc tiếp đúng chuyến đó.
        var laterScan = Now.AddHours(2).AddMinutes(30);
        (await TripReminderSupport.AddDueTripRemindersAsync(context, laterScan, CancellationToken.None))
            .ShouldBe(1);
        var returnReminder = context.Set<Notification>().Single(n => n.RelatedEntityId == tripLater.Id);
        returnReminder.UserId.ShouldBe(roundTripUser);
    }

    [Test]
    public async Task ReminderSkipsCancelledTripsAndIncludesCharterSourceBooking()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var charterUser = Guid.NewGuid();
        var charterBooking = CreateBooking(charterUser, bookingCode: "CB-REM");
        context.Add(charterBooking);
        await context.SaveChangesAsync();
        var charterTrip = CreateTrip(Now.AddMinutes(30), sourceBookingId: charterBooking.Id);
        var cancelledTrip = CreateTrip(Now.AddMinutes(30), TripStatus.Cancelled);
        context.AddRange(charterTrip, cancelledTrip);
        await context.SaveChangesAsync();
        context.Add(CreateBooking(Guid.NewGuid(), tripId: cancelledTrip.Id, bookingCode: "BK-CXL"));
        await context.SaveChangesAsync();

        var created = await TripReminderSupport.AddDueTripRemindersAsync(context, Now, CancellationToken.None);

        created.ShouldBe(1);
        var notification = context.Set<Notification>().Single();
        notification.UserId.ShouldBe(charterUser);
        notification.RelatedEntityId.ShouldBe(charterTrip.Id);
    }
}
