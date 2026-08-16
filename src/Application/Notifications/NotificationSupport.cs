using System.Globalization;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Reviews;
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
    public const string TripCompleted = "trip_completed";
    public const string PromotionNew = "promotion_new";
    public const string IncidentReported = "incident_reported";
    public const string IncidentDispatched = "incident_dispatched";
    public const string IncidentProgress = "incident_progress";
    public const string IncidentResolved = "incident_resolved";
    public const string StaffAssignmentCreated = "staff_assignment_created";
    public const string StaffAssignmentReplaced = "staff_assignment_replaced";
    public const string StaffShiftStarting = "staff_shift_starting";
    public const string StaffShiftEnding = "staff_shift_ending";
    public const string StaffTripUpcoming = "staff_trip_upcoming";
    public const string StaffTripArriving = "staff_trip_arriving";
    public const string StaffTripDeparted = "staff_trip_departed";
    public const string StaffTripArrived = "staff_trip_arrived";
    public const string StaffTripCompleted = "staff_trip_completed";
    public const string StaffTripCancelled = "staff_trip_cancelled";
    public const string StaffPassengerBoarded = "staff_passenger_boarded";
    public const string StaffPassengerAlighted = "staff_passenger_alighted";
    public const string OperationsTripBoarding = "operations_trip_boarding";
    public const string OperationsTripDeparted = "operations_trip_departed";
    public const string OperationsTripCompleted = "operations_trip_completed";
    public const string OperationsTripCancelled = "operations_trip_cancelled";
    public const string OperationsTripDelayed = "operations_trip_delayed";
    public const string TripReplanned = "trip_replanned";
    public const string StaffTripReplanned = "staff_trip_replanned";
    public const string OperationsTripReplanned = "operations_trip_replanned";
    public const string OperationsReplanRequired = "operations_replan_required";
    public const string CharterQuoted = "charter_quoted";
    public const string CharterRequested = "charter_requested";
    public const string CharterPaymentReceived = "charter_payment_received";
    public const string CharterPassengerAddRequested = "charter_passenger_add_requested";
    public const string CharterCancelled = "charter_cancelled";
    public const string CharterCompleted = "charter_completed";
    public const string CharterBoatMaintenanceAffectsBooking = "charter_boat_maintenance_affects_booking";
    public const string BookingCompleted = "booking_completed";
}

public static class NotificationRelatedEntityTypes
{
    public const string Booking = "booking";
    public const string Incident = "incident";
    public const string Trip = "trip";
    public const string Promotion = "promotion";
    public const string StaffAssignment = "staff_assignment";
    public const string Boat = "boat";
}

public static class NotificationSupport
{
    public static Notification AddStaffAssignmentNotification(
        IApplicationDbContext context,
        Guid staffUserId,
        Guid assignmentId,
        string title,
        string body,
        string type,
        DateTimeOffset now)
    {
        var notification = new Notification
        {
            UserId = staffUserId,
            Title = title,
            Body = body,
            Type = type,
            RelatedEntityType = NotificationRelatedEntityTypes.StaffAssignment,
            RelatedEntityId = assignmentId,
            CreatedAt = now
        };
        context.Set<Notification>().Add(notification);
        return notification;
    }

    /// <summary>
    /// Push realtime các notification ĐÃ được SaveChanges thành công tới client đang mở app (SignalR).
    /// Gọi sau save để client không nhận sự kiện cho bản ghi chưa/không tồn tại.
    /// </summary>
    public static async Task PublishCreatedAsync(
        INotificationRealtimeNotifier? notifier,
        IReadOnlyList<Notification> notifications,
        CancellationToken cancellationToken)
    {
        if (notifications.Count == 0)
        {
            return;
        }

        // Realtime (SignalR) - skip nếu không có client online.
        if (notifier is not null)
        {
            await notifier.PublishCreatedAsync(
                notifications.Select(ToRealtimeEvent).ToList(),
                cancellationToken);
        }
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

    public static Notification? AddCharterBookingQuotedNotification(
        IApplicationDbContext context,
        Booking booking,
        DateTimeOffset now)
    {
        if (!booking.UserId.HasValue)
        {
            return null;
        }

        var totalText = FormatAmount(booking.TotalAmount, booking.Currency);
        var amountText = totalText;
        var depositText = booking.DepositAmount > 0
            ? $" Tiền đặt cọc: {FormatAmount(booking.DepositAmount, booking.Currency)}."
            : "";

        var paymentDeadline = booking.HoldExpiresAt.HasValue
            ? $" Vui lòng thanh toán trước {FormatVietnamTime(booking.HoldExpiresAt.Value)} để giữ chỗ."
            : "";

        var notification = new Notification
        {
            UserId = booking.UserId.Value,
            Title = "Đơn thuê tàu đã được chốt giá",
            Body = $"Booking {booking.BookingCode} đã được chốt giá {amountText}.{depositText}{paymentDeadline}",
            Type = NotificationTypes.CharterQuoted,
            RelatedEntityType = NotificationRelatedEntityTypes.Booking,
            RelatedEntityId = booking.Id,
            CreatedAt = now
        };
        context.Set<Notification>().Add(notification);
        return notification;
    }

    /// <summary>
    /// Customer vừa tạo yêu cầu thuê tàu → báo cho admin/manager xử lý.
    /// </summary>
    public static async Task<IReadOnlyList<Notification>> AddCharterBookingRequestedNotificationsAsync(
        IApplicationDbContext context,
        Booking booking,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var adminIds = await LoadAdminManagerRecipientIdsAsync(context, cancellationToken);
        if (adminIds.Count == 0)
        {
            return [];
        }

        var passengerText = booking.PassengerCount.GetValueOrDefault() > 0
            ? $" {booking.PassengerCount} khách"
            : "";
        var body = $"Yêu cầu thuê tàu mới từ {booking.ContactName}{passengerText}. "
            + $"Ngày khởi hành {booking.DepartureDate.GetValueOrDefault():dd/MM/yyyy}.";
        return AddNotifications(
            context,
            adminIds,
            "Yêu cầu thuê tàu mới",
            body,
            NotificationTypes.CharterRequested,
            NotificationRelatedEntityTypes.Booking,
            booking.Id,
            now);
    }

    /// <summary>
    /// Customer vừa thanh toán charter → báo admin/manager (đặc biệt Manager được giao booking) để theo dõi.
    /// </summary>
    public static async Task<IReadOnlyList<Notification>> AddCharterPaymentReceivedNotificationsAsync(
        IApplicationDbContext context,
        Booking booking,
        decimal paidAmount,
        bool isFullyPaid,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var recipientIds = new HashSet<Guid>();
        var adminIds = await LoadAdminManagerRecipientIdsAsync(context, cancellationToken);
        foreach (var id in adminIds)
        {
            recipientIds.Add(id);
        }

        // Kèm Manager được giao booking (nếu có) — kể cả khi role Manager không có trong adminIds chung.
        if (booking.AssignedManagerId.HasValue)
        {
            recipientIds.Add(booking.AssignedManagerId.Value);
        }

        if (recipientIds.Count == 0)
        {
            return [];
        }

        var amountText = FormatAmount(paidAmount, booking.Currency);
        var title = isFullyPaid ? "Charter booking đã thanh toán đủ" : "Charter booking vừa nhận đặt cọc";
        var body = isFullyPaid
            ? $"Booking {booking.BookingCode} đã thanh toán đủ {amountText}."
            : $"Booking {booking.BookingCode} vừa nhận đặt cọc {amountText}.";
        return AddNotifications(
            context,
            recipientIds.ToList(),
            title,
            body,
            NotificationTypes.CharterPaymentReceived,
            NotificationRelatedEntityTypes.Booking,
            booking.Id,
            now);
    }

    /// <summary>
    /// Customer vừa gửi yêu cầu thêm hành khách → báo admin/manager duyệt.
    /// </summary>
    public static async Task<IReadOnlyList<Notification>> AddCharterPassengerAddRequestedNotificationsAsync(
        IApplicationDbContext context,
        Booking booking,
        int addedPassengerCount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var recipientIds = new HashSet<Guid>();
        var adminIds = await LoadAdminManagerRecipientIdsAsync(context, cancellationToken);
        foreach (var id in adminIds)
        {
            recipientIds.Add(id);
        }

        if (booking.AssignedManagerId.HasValue)
        {
            recipientIds.Add(booking.AssignedManagerId.Value);
        }

        if (recipientIds.Count == 0)
        {
            return [];
        }

        var body = $"Booking {booking.BookingCode} có yêu cầu thêm {addedPassengerCount} hành khách. "
            + $"Vui lòng duyệt trước khi chuyến khởi hành.";
        return AddNotifications(
            context,
            recipientIds.ToList(),
            "Yêu cầu thêm hành khách",
            body,
            NotificationTypes.CharterPassengerAddRequested,
            NotificationRelatedEntityTypes.Booking,
            booking.Id,
            now);
    }

    /// <summary>
    /// Charter booking bị hủy → báo customer (nếu có tài khoản) + admin/manager.
    /// </summary>
    public static async Task<IReadOnlyList<Notification>> AddCharterBookingCancelledNotificationsAsync(
        IApplicationDbContext context,
        Booking booking,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var recipientIds = new HashSet<Guid>();
        var adminIds = await LoadAdminManagerRecipientIdsAsync(context, cancellationToken);
        foreach (var id in adminIds)
        {
            recipientIds.Add(id);
        }

        if (booking.UserId.HasValue)
        {
            recipientIds.Add(booking.UserId.Value);
        }

        if (booking.AssignedManagerId.HasValue)
        {
            recipientIds.Add(booking.AssignedManagerId.Value);
        }

        if (recipientIds.Count == 0)
        {
            return [];
        }

        var customerBody = $"Charter booking {booking.BookingCode} đã bị hủy. "
            + $"Mọi chuyến phát sinh từ booking cũng đã bị hủy.";
        var adminBody = booking.UserId.HasValue
            ? $"Charter booking {booking.BookingCode} đã bị hủy. Hành khách đã được thông báo."
            : $"Charter booking {booking.BookingCode} đã bị hủy.";

        var created = new List<Notification>();
        foreach (var recipientId in recipientIds)
        {
            var isCustomer = booking.UserId.HasValue && recipientId == booking.UserId.Value;
            var notification = new Notification
            {
                UserId = recipientId,
                Title = "Charter booking đã bị hủy",
                Body = isCustomer ? customerBody : adminBody,
                Type = NotificationTypes.CharterCancelled,
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
    /// Booking (seat/charter) tự động sang Hoàn tất khi trip tương ứng đã Completed.
    /// Báo cho customer (nếu có tài khoản) + admin/manager + staff phụ trách.
    /// </summary>
    public static async Task<IReadOnlyList<Notification>> AddBookingCompletedNotificationsAsync(
        IApplicationDbContext context,
        Booking booking,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var recipientIds = new HashSet<Guid>();
        var adminIds = await LoadAdminManagerRecipientIdsAsync(context, cancellationToken);
        foreach (var id in adminIds)
        {
            recipientIds.Add(id);
        }

        if (booking.UserId.HasValue)
        {
            recipientIds.Add(booking.UserId.Value);
        }

        if (booking.AssignedManagerId.HasValue)
        {
            recipientIds.Add(booking.AssignedManagerId.Value);
        }

        if (recipientIds.Count == 0)
        {
            return [];
        }

        var isCharter = Booking.IsCharterBookingType(booking.BookingType);
        var customerBody = isCharter
            ? $"Charter booking {booking.BookingCode} đã hoàn tất. Cảm ơn quý khách đã sử dụng dịch vụ."
            : $"Booking {booking.BookingCode} đã hoàn tất. Cảm ơn quý khách đã sử dụng dịch vụ.";
        var adminBody = isCharter
            ? $"Charter booking {booking.BookingCode} đã hoàn tất (auto từ trip Completed)."
            : $"Booking {booking.BookingCode} đã hoàn tất (auto từ trip Completed).";

        var type = isCharter ? NotificationTypes.CharterCompleted : NotificationTypes.BookingCompleted;
        var title = isCharter ? "Charter booking hoàn tất" : "Booking hoàn tất";

        var created = new List<Notification>();
        foreach (var recipientId in recipientIds)
        {
            var isCustomer = booking.UserId.HasValue && recipientId == booking.UserId.Value;
            var notification = new Notification
            {
                UserId = recipientId,
                Title = title,
                Body = isCustomer ? customerBody : adminBody,
                Type = type,
                RelatedEntityType = NotificationRelatedEntityTypes.Booking,
                RelatedEntityId = booking.Id,
                CreatedAt = now
            };
            context.Set<Notification>().Add(notification);
            created.Add(notification);
        }

        return created;
    }

    private static async Task<IReadOnlyList<Guid>> LoadAdminManagerRecipientIdsAsync(
        IApplicationDbContext context,
        CancellationToken cancellationToken) =>
        await context.Set<User>()
            .AsNoTracking()
            .Where(u => u.Status == UserStatus.Active
                && (u.Role.Code == Roles.AdminCode || u.Role.Code == Roles.ManagerCode))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

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
    /// Chuyến vừa chuyển sang Completed → mời khách đánh giá khi toàn bộ booking đã hoàn tất.
    /// Booking khứ hồi chờ cả chiều đi và chiều về Completed; dedup theo (user, booking).
    /// Caller chịu trách nhiệm SaveChanges.
    /// </summary>
    public static async Task<IReadOnlyList<Notification>> AddTripCompletedReviewInviteNotificationsAsync(
        IApplicationDbContext context,
        Trip trip,
        TripStatus oldStatus,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (trip.TripStatus == oldStatus || trip.TripStatus != TripStatus.Completed)
        {
            return [];
        }

        var bookings = await context.Set<Booking>()
            .Include(b => b.Trip!)
            .Include(b => b.ReturnTrip!)
            .Include(b => b.Passengers)
                .ThenInclude(p => p.Trip!)
            .Include(b => b.CharterBoats)
                .ThenInclude(cb => cb.Trip!)
            .Where(b => b.UserId != null
                && (b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Completed)
                && (b.TripId == trip.Id
                    || b.ReturnTripId == trip.Id
                    || b.Passengers.Any(p => p.TripId == trip.Id)
                    || b.CharterBoats.Any(cb => cb.TripId == trip.Id)
                    || (trip.SourceBookingId != null && b.Id == trip.SourceBookingId)))
            .ToListAsync(cancellationToken);

        var reviewableBookings = bookings
            .Where(ReviewSupport.IsServiceCompleted)
            .ToList();
        if (reviewableBookings.Count == 0)
        {
            return [];
        }

        var bookingIds = reviewableBookings.Select(b => b.Id).ToList();
        var alreadyInvitedBookingIds = await context.Set<Notification>()
            .Where(n => n.Type == NotificationTypes.TripCompleted
                && n.RelatedEntityType == NotificationRelatedEntityTypes.Booking
                && n.RelatedEntityId.HasValue
                && bookingIds.Contains(n.RelatedEntityId.Value))
            .Select(n => n.RelatedEntityId!.Value)
            .ToListAsync(cancellationToken);
        var reviewedBookingIds = await context.Set<Review>()
            .Where(r => r.BookingId.HasValue
                && bookingIds.Contains(r.BookingId.Value))
            .Select(r => r.BookingId!.Value)
            .ToListAsync(cancellationToken);

        var tripLabel = DescribeTrip(trip);
        var departureText = FormatVietnamTime(trip.DepartureTime);
        var created = new List<Notification>();
        foreach (var booking in reviewableBookings
            .Where(b => !alreadyInvitedBookingIds.Contains(b.Id)
                && !reviewedBookingIds.Contains(b.Id)))
        {
            var notification = new Notification
            {
                UserId = booking.UserId!.Value,
                Title = "Đánh giá chuyến đi của bạn",
                Body = $"Booking {booking.BookingCode} đã hoàn tất sau {tripLabel} khởi hành lúc {departureText}. "
                    + "Hãy đánh giá trải nghiệm để giúp chúng tôi phục vụ tốt hơn.",
                Type = NotificationTypes.TripCompleted,
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

    public static async Task<IReadOnlyList<Notification>> AddIncidentReportedNotificationsAsync(
        IApplicationDbContext context,
        Incident incident,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var recipientIds = await LoadOperationalRecipientIdsAsync(context, cancellationToken);
        if (recipientIds.Count == 0)
        {
            return [];
        }

        var title = "Sự cố tàu mới";
        var body = $"{DescribeIncident(incident)} vừa được ghi nhận. "
            + "Vui lòng kiểm tra và điều tàu cứu hộ/thay thế nếu cần.";
        return AddNotifications(
            context,
            recipientIds,
            title,
            body,
            NotificationTypes.IncidentReported,
            NotificationRelatedEntityTypes.Incident,
            incident.Id,
            now);
    }

    public static async Task<IReadOnlyList<Notification>> AddIncidentDispatchedNotificationsAsync(
        IApplicationDbContext context,
        Incident incident,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var created = new List<Notification>();
        var operationalRecipients = await LoadOperationalRecipientIdsAsync(context, cancellationToken);
        if (operationalRecipients.Count > 0)
        {
            var supportText = DescribeIncidentSupport(incident);
            created.AddRange(AddNotifications(
                context,
                operationalRecipients,
                "Đã điều tàu xử lý sự cố",
                $"{DescribeIncident(incident)}. {supportText}",
                NotificationTypes.IncidentDispatched,
                NotificationRelatedEntityTypes.Incident,
                incident.Id,
                now));
        }

        var affectedBookings = await LoadAffectedConfirmedBookingsAsync(context, incident, cancellationToken);
        foreach (var booking in affectedBookings)
        {
            var notification = new Notification
            {
                UserId = booking.UserId,
                Title = "Chuyến đi đang được hỗ trợ",
                Body = $"{DescribeCustomerTrip(incident)} đang được xử lý sự cố. "
                    + $"{DescribeCustomerSupport(incident)} Booking {booking.BookingCode} của bạn bị ảnh hưởng, vui lòng theo dõi thông báo giờ chạy mới.",
                Type = NotificationTypes.IncidentDispatched,
                RelatedEntityType = NotificationRelatedEntityTypes.Booking,
                RelatedEntityId = booking.BookingId,
                CreatedAt = now
            };
            context.Set<Notification>().Add(notification);
            created.Add(notification);
        }

        return created;
    }

    public static async Task<IReadOnlyList<Notification>> AddIncidentProgressNotificationsAsync(
        IApplicationDbContext context,
        Incident incident,
        string eventType,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var recipientIds = await LoadOperationalRecipientIdsAsync(context, cancellationToken);
        if (recipientIds.Count == 0)
        {
            return [];
        }

        var progressText = eventType switch
        {
            "RescueArrived" => "Tàu cứu hộ đã tới vị trí sự cố.",
            "ReplacementArrived" => "Tàu thay thế đã tới điểm tiếp nhận.",
            "PassengerTransferCompleted" => "Đã hoàn tất chuyển khách sang tàu thay thế.",
            "TowingStarted" => "Tàu cứu hộ đã bắt đầu lai dắt tàu gặp sự cố.",
            "TowingCompleted" => "Đã hoàn tất lai dắt tàu gặp sự cố.",
            _ => $"GPS cập nhật tiến độ: {eventType}."
        };
        return AddNotifications(
            context,
            recipientIds,
            "Cập nhật xử lý sự cố",
            $"{DescribeIncident(incident)}. {progressText}",
            NotificationTypes.IncidentProgress,
            NotificationRelatedEntityTypes.Incident,
            incident.Id,
            now);
    }

    public static async Task<IReadOnlyList<Notification>> AddIncidentResolvedNotificationsAsync(
        IApplicationDbContext context,
        Incident incident,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var created = new List<Notification>();
        var operationalRecipients = await LoadOperationalRecipientIdsAsync(context, cancellationToken);
        if (operationalRecipients.Count > 0)
        {
            created.AddRange(AddNotifications(
                context,
                operationalRecipients,
                "Sự cố đã được xử lý",
                $"{DescribeIncident(incident)} đã được đóng. {NormalizeOptional(incident.ResolutionNote) ?? "Vui lòng kiểm tra trạng thái tàu/chuyến sau xử lý."}",
                NotificationTypes.IncidentResolved,
                NotificationRelatedEntityTypes.Incident,
                incident.Id,
                now));
        }

        var affectedBookings = await LoadAffectedConfirmedBookingsAsync(context, incident, cancellationToken);
        foreach (var booking in affectedBookings)
        {
            var notification = new Notification
            {
                UserId = booking.UserId,
                Title = "Sự cố chuyến đi đã được xử lý",
                Body = $"{DescribeCustomerTrip(incident)} đã được xử lý sự cố. "
                    + "Vui lòng kiểm tra lại giờ chạy/trạng thái vé trước khi ra bến.",
                Type = NotificationTypes.IncidentResolved,
                RelatedEntityType = NotificationRelatedEntityTypes.Booking,
                RelatedEntityId = booking.BookingId,
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

    private static async Task<IReadOnlyList<Guid>> LoadOperationalRecipientIdsAsync(
        IApplicationDbContext context,
        CancellationToken cancellationToken) =>
        await context.Set<User>()
            .AsNoTracking()
            .Where(u => u.Status == UserStatus.Active
                && (u.Role.Code == Roles.AdminCode
                    || u.Role.Code == Roles.ManagerCode
                    || u.Role.Code == Roles.StaffCode))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

    private static IReadOnlyList<Notification> AddNotifications(
        IApplicationDbContext context,
        IReadOnlyList<Guid> userIds,
        string title,
        string body,
        string type,
        string relatedEntityType,
        Guid relatedEntityId,
        DateTimeOffset now)
    {
        var created = new List<Notification>(userIds.Count);
        foreach (var userId in userIds.Distinct())
        {
            var notification = new Notification
            {
                UserId = userId,
                Title = title,
                Body = body,
                Type = type,
                RelatedEntityType = relatedEntityType,
                RelatedEntityId = relatedEntityId,
                CreatedAt = now
            };
            context.Set<Notification>().Add(notification);
            created.Add(notification);
        }

        return created;
    }

    private static async Task<IReadOnlyList<AffectedBookingNotificationRecipient>> LoadAffectedConfirmedBookingsAsync(
        IApplicationDbContext context,
        Incident incident,
        CancellationToken cancellationToken)
    {
        if (!incident.TripId.HasValue)
        {
            return [];
        }

        var affectedBookings = await context.Set<Booking>()
            .AsNoTracking()
            .Where(b => b.UserId != null
                && b.BookingStatus == BookingStatus.Confirmed
                && (b.TripId == incident.TripId.Value
                    || b.ReturnTripId == incident.TripId.Value
                    || b.Passengers.Any(p => p.TripId == incident.TripId.Value)
                    || (incident.Trip != null && incident.Trip.SourceBookingId != null && b.Id == incident.Trip.SourceBookingId)))
            .Select(b => new AffectedBookingNotificationRecipient(
                b.Id,
                b.UserId!.Value,
                b.BookingCode))
            .ToListAsync(cancellationToken);

        return affectedBookings
            .GroupBy(b => b.BookingId)
            .Select(g => g.First())
            .ToList();
    }

    private static string DescribeIncident(Incident incident)
    {
        var boatName = string.IsNullOrWhiteSpace(incident.Boat?.Name)
            ? "tàu"
            : $"tàu {incident.Boat.Name}";
        var tripText = incident.Trip is null
            ? ""
            : $" trên chuyến {incident.Trip.TripCode}";
        var severityText = string.IsNullOrWhiteSpace(incident.Severity)
            ? ""
            : $" Mức độ: {incident.Severity}.";
        return $"Sự cố {incident.IncidentType} của {boatName}{tripText}.{severityText}";
    }

    private static string DescribeCustomerTrip(Incident incident) =>
        incident.Trip is null
            ? "Chuyến đi của bạn"
            : $"Chuyến {incident.Trip.TripCode} của bạn";

    private static string DescribeIncidentSupport(Incident incident)
    {
        var rescueText = incident.RescueBoat is null
            ? "Chưa có tàu cứu hộ."
            : $"Tàu cứu hộ: {incident.RescueBoat.Name}.";
        var replacementText = incident.ReplacementBoat is null
            ? " Chưa có tàu thay thế."
            : $" Tàu thay thế: {incident.ReplacementBoat.Name}.";
        var delayText = incident.ReplacementDelayMinutes > 0
            ? $" Dự kiến trễ {incident.ReplacementDelayMinutes} phút."
            : "";
        return rescueText + replacementText + delayText;
    }

    private static string DescribeCustomerSupport(Incident incident)
    {
        if (incident.ReplacementBoat is not null)
        {
            return $"Hệ thống đã điều tàu thay thế {incident.ReplacementBoat.Name}.";
        }

        if (incident.RescueBoat is not null)
        {
            return "Hệ thống đã điều tàu cứu hộ.";
        }

        return "Đội vận hành đang xử lý.";
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Tàu phục vụ charter booking vừa chuyển sang UnderMaintenance (hoặc admin xếp tàu bảo trì,
    /// incident rescue, ...) → báo cho admin/manager biết để đổi tàu cho các booking đã xác nhận.
    /// Đồng thời báo cho customer của từng booking để khách hàng yên tâm hệ thống đang xử lý.
    /// Caller chịu trách nhiệm SaveChanges rồi gọi <see cref="PublishCreatedAsync"/>.
    /// </summary>
    public static async Task<IReadOnlyList<Notification>> AddCharterBoatMaintenanceAffectsBookingNotificationsAsync(
        IApplicationDbContext context,
        Boat boat,
        DateTimeOffset? estimatedMaintenanceEndAt,
        string? maintenanceNote,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Booking Quoted/Confirmed dùng tàu này (qua BoatId hoặc CharterBoats) còn departureDate ở tương lai.
        var futureBookings = await context.Set<Booking>()
            .Where(b => Booking.IsCharterBookingType(b.BookingType)
                && b.DepartureDate.HasValue
                && b.DepartureDate.Value >= DateOnly.FromDateTime(now.UtcDateTime)
                && (b.BookingStatus == BookingStatus.Confirmed
                    || b.BookingStatus == BookingStatus.Quoted)
                && (b.BoatId == boat.Id
                    || context.Set<CharterBookingBoat>().Any(cb => cb.BookingId == b.Id && cb.BoatId == boat.Id)))
            .Select(b => new
            {
                b.Id,
                b.BookingCode,
                b.ContactName,
                b.DepartureDate,
                b.UserId,
                b.AssignedManagerId
            })
            .ToListAsync(cancellationToken);

        if (futureBookings.Count == 0)
        {
            return [];
        }

        var created = new List<Notification>();
        var adminIds = await LoadAdminManagerRecipientIdsAsync(context, cancellationToken);
        var boatLabel = $"tàu {boat.Name}";
        var maintenanceHint = estimatedMaintenanceEndAt.HasValue
            ? $" Dự kiến hoàn tất bảo trì lúc {estimatedMaintenanceEndAt:dd/MM/yyyy HH:mm}."
            : (!string.IsNullOrWhiteSpace(maintenanceNote) ? $" Ghi chú: {maintenanceNote.Trim()}." : string.Empty);

        // 1. Notification cho admin/manager (gom chung vào 1 group action items).
        if (adminIds.Count > 0)
        {
            var bookingCodes = string.Join(", ", futureBookings.Take(5).Select(x => x.BookingCode));
            var moreText = futureBookings.Count > 5 ? $" và {futureBookings.Count - 5} booking khác" : string.Empty;
            var adminBody = $"{boatLabel} vừa chuyển sang bảo trì.{maintenanceHint} "
                + $"Cần đổi tàu cho {futureBookings.Count} charter booking sắp tới: {bookingCodes}{moreText}.";
            created.AddRange(AddNotifications(
                context,
                adminIds,
                "Tàu charter vào bảo trì — cần đổi tàu",
                adminBody,
                NotificationTypes.CharterBoatMaintenanceAffectsBooking,
                NotificationRelatedEntityTypes.Boat,
                boat.Id,
                now));
        }

        // 2. Notification riêng cho từng manager được giao booking (nếu chưa nằm trong adminIds).
        var assignedManagerIds = futureBookings
            .Where(b => b.AssignedManagerId.HasValue)
            .Select(b => b.AssignedManagerId!.Value)
            .Where(id => !adminIds.Contains(id))
            .Distinct()
            .ToList();
        if (assignedManagerIds.Count > 0)
        {
            var managerBody = $"{boatLabel} bạn đang dùng cho các charter booking vừa vào bảo trì.{maintenanceHint} "
                + $"Vui lòng phối hợp admin đổi tàu cho {futureBookings.Count} booking.";
            created.AddRange(AddNotifications(
                context,
                assignedManagerIds,
                "Tàu charter bạn phụ trách vào bảo trì",
                managerBody,
                NotificationTypes.CharterBoatMaintenanceAffectsBooking,
                NotificationRelatedEntityTypes.Boat,
                boat.Id,
                now));
        }

        // 3. Notification cho customer của từng booking (chỉ booking có UserId, có booking account).
        var customerNotifications = new List<(Guid userId, string body, Guid bookingId)>();
        foreach (var booking in futureBookings.Where(b => b.UserId.HasValue))
        {
            var departureText = booking.DepartureDate.HasValue
                ? $" ngày {booking.DepartureDate.Value:dd/MM/yyyy}"
                : string.Empty;
            var customerBody = $"Charter booking {booking.BookingCode} của bạn{departureText} đang được hệ thống điều phối lại tàu "
                + $"do {boatLabel} vào bảo trì.{maintenanceHint} "
                + "Đội ngũ sẽ liên hệ bạn trong thời gian sớm nhất để xác nhận phương án.";
            customerNotifications.Add((booking.UserId!.Value, customerBody, booking.Id));
        }

        foreach (var item in customerNotifications)
        {
            created.AddRange(AddNotifications(
                context,
                [item.userId],
                "Charter booking đang được điều phối lại",
                item.body,
                NotificationTypes.CharterBoatMaintenanceAffectsBooking,
                NotificationRelatedEntityTypes.Booking,
                item.bookingId,
                now));
        }

        return created;
    }

    private static string FormatAmount(decimal amount, string currency) =>
        string.Create(CultureInfo.GetCultureInfo("vi-VN"), $"{amount:N0} {currency}");

    private sealed record AffectedBookingNotificationRecipient(
        Guid BookingId,
        Guid UserId,
        string BookingCode);
}
