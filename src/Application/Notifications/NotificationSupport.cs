using System.Globalization;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Notifications;

public static class NotificationTypes
{
    public const string BookingConfirmed = "booking_confirmed";
    public const string TripCancelled = "trip_cancelled";
    public const string TripDelayed = "trip_delayed";
    public const string TripReminder = "trip_reminder";
    public const string PromotionNew = "promotion_new";
}

public static class NotificationRelatedEntityTypes
{
    public const string Booking = "booking";
    public const string Trip = "trip";
    public const string Promotion = "promotion";
}

public static class NotificationSupport
{
    /// <summary>
    /// Push realtime các notification ĐÃ được SaveChanges thành công. Gọi sau save để
    /// client không nhận sự kiện cho bản ghi chưa/không tồn tại.
    /// </summary>
    public static Task PublishCreatedAsync(
        INotificationRealtimeNotifier? notifier,
        IReadOnlyList<Notification> notifications,
        CancellationToken cancellationToken)
    {
        if (notifier is null || notifications.Count == 0)
        {
            return Task.CompletedTask;
        }

        return notifier.PublishCreatedAsync(
            notifications.Select(ToRealtimeEvent).ToList(),
            cancellationToken);
    }

    public static NotificationRealtimeEvent ToRealtimeEvent(Notification notification) =>
        new(
            notification.Id,
            notification.UserId,
            notification.Title,
            notification.Body,
            notification.Type,
            notification.RelatedEntityType,
            notification.RelatedEntityId,
            notification.CreatedAt);

    /// <summary>
    /// Tạo in-app notification khi payment vừa chuyển sang Paid. Booking khách vãng lai
    /// (không có UserId) không có hộp thông báo nên bỏ qua. Caller chịu trách nhiệm SaveChanges.
    /// </summary>
    public static Notification? AddBookingPaymentSucceededNotification(
        IApplicationDbContext context,
        Booking booking,
        Payment payment,
        DateTimeOffset now)
    {
        if (!booking.UserId.HasValue)
        {
            return null;
        }

        var amountText = FormatAmount(payment.Amount, booking.Currency);
        var isFullyPaid = booking.RemainingAmount <= 0;
        string title;
        string body;
        if (isFullyPaid)
        {
            title = "Thanh toán thành công";
            body = Booking.IsCharterBookingType(booking.BookingType)
                ? $"Booking {booking.BookingCode} đã thanh toán đủ {amountText}. Đơn thuê tàu của bạn đã được xác nhận."
                : $"Booking {booking.BookingCode} đã thanh toán {amountText}. Vé của bạn đã được xác nhận và gửi tới email liên hệ.";
        }
        else
        {
            title = "Đã nhận tiền đặt cọc";
            body = $"Booking {booking.BookingCode} đã nhận đặt cọc {amountText}. "
                + $"Số tiền còn lại: {FormatAmount(booking.RemainingAmount, booking.Currency)}.";
        }

        var notification = new Notification
        {
            UserId = booking.UserId.Value,
            Title = title,
            Body = body,
            Type = NotificationTypes.BookingConfirmed,
            RelatedEntityType = NotificationRelatedEntityTypes.Booking,
            RelatedEntityId = booking.Id,
            CreatedAt = now
        };
        context.Set<Notification>().Add(notification);
        return notification;
    }

    /// <summary>
    /// Chuyến chuyển sang Cancelled/Delayed → tạo thông báo cho mọi booking Confirmed trên chuyến
    /// (chiều đi, chiều về, và charter booking sinh ra chuyến). Chỉ bắn khi trạng thái thực sự đổi.
    /// Caller chịu trách nhiệm SaveChanges.
    /// </summary>
    public static async Task<IReadOnlyList<Notification>> AddTripStatusChangedNotificationsAsync(
        IApplicationDbContext context,
        Trip trip,
        TripStatus oldStatus,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (trip.TripStatus == oldStatus
            || (trip.TripStatus != TripStatus.Cancelled && trip.TripStatus != TripStatus.Delayed))
        {
            return [];
        }

        var affectedBookings = await context.Set<Booking>()
            .Where(b => b.UserId != null
                && b.BookingStatus == BookingStatus.Confirmed
                && (b.TripId == trip.Id
                    || b.ReturnTripId == trip.Id
                    || (trip.SourceBookingId != null && b.Id == trip.SourceBookingId)))
            .Select(b => new { b.Id, UserId = b.UserId!.Value, b.BookingCode })
            .ToListAsync(cancellationToken);

        if (affectedBookings.Count == 0)
        {
            return [];
        }

        var tripLabel = DescribeTrip(trip);
        var departureText = FormatVietnamTime(trip.DepartureTime);
        var isCancelled = trip.TripStatus == TripStatus.Cancelled;
        var reasonText = string.IsNullOrWhiteSpace(trip.StatusNote) ? "" : $" Lý do: {trip.StatusNote.Trim()}.";
        var created = new List<Notification>(affectedBookings.Count);
        foreach (var booking in affectedBookings)
        {
            var body = isCancelled
                ? $"{tripLabel} khởi hành lúc {departureText} đã bị hủy. Booking {booking.BookingCode} của bạn bị ảnh hưởng, vui lòng liên hệ hỗ trợ.{reasonText}"
                : $"{tripLabel} (giờ khởi hành dự kiến {departureText}) đang bị trễ. Booking {booking.BookingCode} của bạn bị ảnh hưởng.{reasonText}";
            var notification = new Notification
            {
                UserId = booking.UserId,
                Title = isCancelled ? "Chuyến đi bị hủy" : "Chuyến đi bị trễ",
                Body = body,
                Type = isCancelled ? NotificationTypes.TripCancelled : NotificationTypes.TripDelayed,
                RelatedEntityType = NotificationRelatedEntityTypes.Booking,
                RelatedEntityId = booking.Id,
                CreatedAt = now
            };
            context.Set<Notification>().Add(notification);
            created.Add(notification);
        }

        return created;
    }

    /// <summary>
    /// Khuyến mãi Public vừa phát hành (Active) → broadcast cho toàn bộ khách hàng đang hoạt động.
    /// Mỗi khuyến mãi chỉ broadcast đúng 1 lần: dedup bằng notification promotion_new + related
    /// promotion id đã lưu, nên Paused → Active bật lại không bắn trùng. Caller SaveChanges.
    /// </summary>
    public static async Task<IReadOnlyList<Notification>> AddPromotionAnnouncementNotificationsAsync(
        IApplicationDbContext context,
        Promotion promotion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (promotion.Status != PromotionStatus.Active || promotion.Visibility != PromotionVisibility.Public)
        {
            return [];
        }

        var alreadyAnnounced = await context.Set<Notification>()
            .AnyAsync(
                n => n.Type == NotificationTypes.PromotionNew && n.RelatedEntityId == promotion.Id,
                cancellationToken);
        if (alreadyAnnounced)
        {
            return [];
        }

        var customerIds = await context.Set<User>()
            .Where(u => u.Status == UserStatus.Active && u.Role.Code == Roles.CustomerCode)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        if (customerIds.Count == 0)
        {
            return [];
        }

        var discountText = promotion.PromotionType == PromotionType.Percent
            ? $"giảm {promotion.DiscountValue.ToString("0.##", CultureInfo.GetCultureInfo("vi-VN"))}%"
                + (promotion.MaxDiscountAmount.HasValue
                    ? $" (tối đa {FormatAmount(promotion.MaxDiscountAmount.Value, "VND")})"
                    : "")
            : $"giảm {FormatAmount(promotion.DiscountValue, "VND")}";
        var body = $"{promotion.PromotionName}: {discountText} với mã {promotion.PromotionCode}. "
            + $"Áp dụng từ {FormatVietnamDate(promotion.ValidFrom)} đến {FormatVietnamDate(promotion.ValidTo)}.";
        var created = new List<Notification>(customerIds.Count);
        foreach (var userId in customerIds)
        {
            var notification = new Notification
            {
                UserId = userId,
                Title = "Khuyến mãi mới",
                Body = body,
                Type = NotificationTypes.PromotionNew,
                RelatedEntityType = NotificationRelatedEntityTypes.Promotion,
                RelatedEntityId = promotion.Id,
                CreatedAt = now
            };
            context.Set<Notification>().Add(notification);
            created.Add(notification);
        }

        return created;
    }

    internal static string DescribeTrip(Trip trip) =>
        string.IsNullOrWhiteSpace(trip.Route?.RouteName)
            ? $"Chuyến {trip.TripCode}"
            : $"Chuyến {trip.TripCode} tuyến {trip.Route.RouteName}";

    internal static string FormatVietnamTime(DateTimeOffset value) =>
        value.ToOffset(VietnamOffset).ToString("HH:mm 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture);

    internal static string FormatVietnamDate(DateTimeOffset value) =>
        value.ToOffset(VietnamOffset).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private static string FormatAmount(decimal amount, string currency) =>
        string.Create(CultureInfo.GetCultureInfo("vi-VN"), $"{amount:N0} {currency}");
}
