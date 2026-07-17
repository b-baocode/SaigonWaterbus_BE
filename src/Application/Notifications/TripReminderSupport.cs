using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Notifications;

/// <summary>
/// Nhắc khách trước giờ khởi hành: quét chuyến Scheduled/Boarding khởi hành trong vòng
/// <see cref="ReminderLeadTime"/> tới, tạo thông báo cho user có booking Confirmed trên chuyến
/// (mỗi user mỗi chuyến đúng 1 lần — dedup bằng notification type + related trip id đã lưu).
/// </summary>
public static class TripReminderSupport
{
    public static readonly TimeSpan ReminderLeadTime = TimeSpan.FromMinutes(60);

    public static async Task<int> AddDueTripRemindersAsync(
        IApplicationDbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        INotificationRealtimeNotifier? realtimeNotifier = null)
    {
        var windowEnd = now + ReminderLeadTime;
        var dueTrips = await context.Set<Trip>()
            .Include(t => t.Route)
            .Where(t => t.DepartureTime > now
                && t.DepartureTime <= windowEnd
                && (t.TripStatus == TripStatus.Scheduled || t.TripStatus == TripStatus.Boarding))
            .ToListAsync(cancellationToken);
        if (dueTrips.Count == 0)
        {
            return 0;
        }

        var tripIds = dueTrips.Select(t => t.Id).ToList();
        var seatBookings = await context.Set<Booking>()
            .Where(b => b.UserId != null
                && b.BookingStatus == BookingStatus.Confirmed
                && ((b.TripId != null && tripIds.Contains(b.TripId.Value))
                    || (b.ReturnTripId != null && tripIds.Contains(b.ReturnTripId.Value))))
            .Select(b => new { UserId = b.UserId!.Value, b.TripId, b.ReturnTripId })
            .ToListAsync(cancellationToken);

        var pairs = new HashSet<(Guid UserId, Guid TripId)>();
        var tripIdSet = tripIds.ToHashSet();
        foreach (var booking in seatBookings)
        {
            if (booking.TripId.HasValue && tripIdSet.Contains(booking.TripId.Value))
            {
                pairs.Add((booking.UserId, booking.TripId.Value));
            }

            if (booking.ReturnTripId.HasValue && tripIdSet.Contains(booking.ReturnTripId.Value))
            {
                pairs.Add((booking.UserId, booking.ReturnTripId.Value));
            }
        }

        // Chuyến charter sinh từ booking: nhắc chủ booking đó (liên kết qua SourceBookingId,
        // không qua booking.TripId).
        var sourceBookingIds = dueTrips
            .Where(t => t.SourceBookingId.HasValue)
            .Select(t => t.SourceBookingId!.Value)
            .Distinct()
            .ToList();
        if (sourceBookingIds.Count > 0)
        {
            var sourceBookingUsers = await context.Set<Booking>()
                .Where(b => b.UserId != null
                    && b.BookingStatus == BookingStatus.Confirmed
                    && sourceBookingIds.Contains(b.Id))
                .Select(b => new { b.Id, UserId = b.UserId!.Value })
                .ToDictionaryAsync(b => b.Id, b => b.UserId, cancellationToken);
            foreach (var trip in dueTrips)
            {
                if (trip.SourceBookingId.HasValue
                    && sourceBookingUsers.TryGetValue(trip.SourceBookingId.Value, out var userId))
                {
                    pairs.Add((userId, trip.Id));
                }
            }
        }

        if (pairs.Count == 0)
        {
            return 0;
        }

        var userIds = pairs.Select(p => p.UserId).Distinct().ToList();
        var alreadyReminded = (await context.Set<Notification>()
                .Where(n => n.Type == NotificationTypes.TripReminder
                    && userIds.Contains(n.UserId)
                    && n.RelatedEntityId != null
                    && tripIds.Contains(n.RelatedEntityId.Value))
                .Select(n => new { n.UserId, n.RelatedEntityId })
                .ToListAsync(cancellationToken))
            .Select(n => (n.UserId, n.RelatedEntityId!.Value))
            .ToHashSet();

        var tripsById = dueTrips.ToDictionary(t => t.Id);
        var created = new List<Notification>();
        foreach (var (userId, tripId) in pairs)
        {
            if (alreadyReminded.Contains((userId, tripId)))
            {
                continue;
            }

            var trip = tripsById[tripId];
            var notification = new Notification
            {
                UserId = userId,
                Title = "Sắp đến giờ khởi hành",
                Body = $"{NotificationSupport.DescribeTrip(trip)} sẽ khởi hành lúc "
                    + $"{NotificationSupport.FormatVietnamTime(trip.DepartureTime)}. "
                    + "Vui lòng có mặt tại bến trước 15 phút.",
                Type = NotificationTypes.TripReminder,
                RelatedEntityType = NotificationRelatedEntityTypes.Trip,
                RelatedEntityId = tripId,
                CreatedAt = now
            };
            context.Set<Notification>().Add(notification);
            created.Add(notification);
        }

        if (created.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            await NotificationSupport.PublishCreatedAsync(realtimeNotifier, created, cancellationToken);
        }

        return created.Count;
    }
}
