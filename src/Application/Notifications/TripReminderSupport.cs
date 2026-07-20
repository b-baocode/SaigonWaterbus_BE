using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Notifications;

/// <summary>
/// Nhắc khách trước giờ lên tàu: tạo thông báo cho user có booking Confirmed khi tới gần
/// <see cref="ReminderLeadTime"/> trước giờ tàu rời BẾN KHÁCH LÊN (không phải bến đầu tuyến —
/// khách lên bến giữa trước đây bị nhắc quá sớm và nhận sai giờ). Mỗi user mỗi chuyến đúng 1 lần
/// — dedup bằng notification type + related trip id đã lưu.
/// </summary>
public static class TripReminderSupport
{
    public static readonly TimeSpan ReminderLeadTime = TimeSpan.FromMinutes(60);

    private sealed record DueReminder(Guid UserId, Guid TripId, DateTimeOffset BoardingTime, string? StationName);

    public static async Task<int> AddDueTripRemindersAsync(
        IApplicationDbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        INotificationRealtimeNotifier? realtimeNotifier = null)
    {
        var windowEnd = now + ReminderLeadTime;

        // Quét mọi chuyến CHƯA kết thúc, không chỉ chuyến chưa rời bến đầu: khách lên bến giữa
        // vẫn cần được nhắc sau khi tàu đã xuất bến. Lọc theo giờ lên tàu làm ở dưới.
        var candidateTrips = await context.Set<Trip>()
            .Include(t => t.Route)
                .ThenInclude(r => r.RouteStops)
            .Include(t => t.TripStops)
            .Where(t => t.ArrivalTime > now
                && (t.TripStatus == TripStatus.Scheduled
                    || t.TripStatus == TripStatus.Boarding
                    || t.TripStatus == TripStatus.InProgress
                    || t.TripStatus == TripStatus.Delayed))
            .ToListAsync(cancellationToken);
        if (candidateTrips.Count == 0)
        {
            return 0;
        }

        var dueTrips = candidateTrips;
        var tripIds = dueTrips.Select(t => t.Id).ToList();
        var tripsById = dueTrips.ToDictionary(t => t.Id);

        // Ai đi chuyến nào: vẫn suy từ Booking.TripId/ReturnTripId để booking cũ chưa có
        // booking_passengers.trip_id không bị bỏ sót nhắc.
        var seatBookings = await context.Set<Booking>()
            .Where(b => b.UserId != null
                && b.BookingStatus == BookingStatus.Confirmed
                && ((b.TripId != null && tripIds.Contains(b.TripId.Value))
                    || (b.ReturnTripId != null && tripIds.Contains(b.ReturnTripId.Value))))
            .Select(b => new { UserId = b.UserId!.Value, b.TripId, b.ReturnTripId })
            .ToListAsync(cancellationToken);

        var tripIdSet = tripIds.ToHashSet();
        var pairs = new HashSet<(Guid UserId, Guid TripId)>();
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

        // Chặng của khách quyết định giờ LÊN TÀU. Một user có thể có nhiều vé trên cùng chuyến
        // (đi cùng gia đình) → lấy chặng lên SỚM NHẤT.
        var passengerLegs = await context.Set<BookingPassenger>()
            .Where(p => p.TripId != null
                && tripIds.Contains(p.TripId.Value)
                && p.Booking.UserId != null
                && p.Booking.BookingStatus == BookingStatus.Confirmed)
            .Select(p => new
            {
                UserId = p.Booking.UserId!.Value,
                TripId = p.TripId!.Value,
                p.FromStopOrder,
                p.ToStopOrder,
                StationName = p.FromStation != null ? p.FromStation.StationName : null
            })
            .ToListAsync(cancellationToken);

        var boardingByPair = new Dictionary<(Guid UserId, Guid TripId), (DateTimeOffset Time, string? Station)>();
        foreach (var leg in passengerLegs)
        {
            var boarding = Trips.BookingCutoffSupport.ResolveBoardingTime(
                tripsById[leg.TripId], leg.FromStopOrder, leg.ToStopOrder);
            var key = (leg.UserId, leg.TripId);
            if (!boardingByPair.TryGetValue(key, out var existing) || boarding < existing.Time)
            {
                boardingByPair[key] = (boarding, leg.StationName);
            }
        }

        var dueByPair = new Dictionary<(Guid UserId, Guid TripId), DueReminder>();
        foreach (var pair in pairs)
        {
            // Không có dòng passenger nào (dữ liệu cũ) → rơi về giờ rời bến đầu như trước.
            var (boarding, station) = boardingByPair.TryGetValue(pair, out var found)
                ? found
                : (tripsById[pair.TripId].DepartureTime, null);
            if (boarding <= now || boarding > windowEnd)
            {
                continue;
            }

            dueByPair[pair] = new DueReminder(pair.UserId, pair.TripId, boarding, station);
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
                // Charter thuê trọn tàu: khách đi từ bến đầu nên mốc vẫn là giờ khởi hành chuyến.
                if (trip.SourceBookingId.HasValue
                    && trip.DepartureTime > now
                    && trip.DepartureTime <= windowEnd
                    && sourceBookingUsers.TryGetValue(trip.SourceBookingId.Value, out var userId))
                {
                    dueByPair.TryAdd(
                        (userId, trip.Id),
                        new DueReminder(userId, trip.Id, trip.DepartureTime, null));
                }
            }
        }

        pairs = dueByPair.Keys.ToHashSet();

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

        var created = new List<Notification>();
        foreach (var due in dueByPair.Values)
        {
            if (alreadyReminded.Contains((due.UserId, due.TripId)))
            {
                continue;
            }

            var trip = tripsById[due.TripId];
            // Giờ và bến của CHÍNH khách đó, không phải giờ rời bến đầu tuyến.
            var placeText = string.IsNullOrWhiteSpace(due.StationName)
                ? "bến"
                : $"bến {due.StationName.Trim()}";
            var notification = new Notification
            {
                UserId = due.UserId,
                Title = "Sắp đến giờ lên tàu",
                Body = $"{NotificationSupport.DescribeTrip(trip)} sẽ rời {placeText} lúc "
                    + $"{NotificationSupport.FormatVietnamTime(due.BoardingTime)}. "
                    + "Vui lòng có mặt trước 15 phút.",
                Type = NotificationTypes.TripReminder,
                RelatedEntityType = NotificationRelatedEntityTypes.Trip,
                RelatedEntityId = due.TripId,
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
