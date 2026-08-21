using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Common;

/// <summary>
/// Lý do bị chặn, gửi cho client tự dịch ra câu tiếng Việt hợp cảnh.
///
/// ĐỂ Ở COMMON, KHÔNG Ở TourGuide: màn hướng dẫn viên lấy dữ liệu qua
/// <c>Application.Landmarks</c>, mà <c>Application.TourGuide</c> lại đọc ngược sang Landmarks để
/// lấy gợi ý tên địa danh. Đặt lớp gác cửa ở một trong hai bên là hai thư mục nhìn vòng vào nhau;
/// để ở đây thì cả hai cùng nhìn xuống.
/// </summary>
public static class TourGuideAccessReasons
{
    public const string Allowed = "allowed";
    public const string Unauthenticated = "unauthenticated";
    public const string NoTicket = "no_ticket";
    public const string NotCheckedIn = "not_checked_in";
    public const string CheckedOut = "checked_out";
    public const string SessionExpired = "session_expired";
}

/// <summary>
/// Quyền dùng hướng dẫn viên AI trên MỘT chuyến.
/// <paramref name="ExpiresAt"/> chỉ để client đếm ngược — KHÔNG phải thứ cưỡng chế; xem ghi chú
/// ở <see cref="TourGuideAccessSupport"/>.
/// </summary>
public sealed record TourGuideAccess(
    bool Allowed,
    string ReasonCode,
    Guid? TripId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// Khách chỉ được hỏi hướng dẫn viên AI khi đang thật sự ngồi trên tàu: có vé của chính tài
/// khoản mình cho đúng chuyến đó, và vé đang ở trạng thái <see cref="TicketStatus.CheckedIn"/>.
///
/// CƯỠNG CHẾ CHỈ DỰA VÀO TRẠNG THÁI VÉ, KHÔNG TỰ TÍNH HẠN GIỜ. Vé check-out là chết ngay; vé
/// khách quên check-out thì job dọn vé nền tự chuyển sang Expired (chậm nhất 60 giây sau hạn,
/// xem <see cref="TicketAttendanceWindowSupport"/>). Tự dựng thêm một mốc giờ ở đây là tạo
/// nguồn sự thật thứ hai, và hai công thức sẽ lệch nhau ngay lần đầu có người sửa một bên.
///
/// <see cref="TourGuideAccess.ExpiresAt"/> vẫn trả về, nhưng thuần tuý để client hiện đồng hồ
/// đếm ngược — nó lấy đúng hạn của vé chứ không phải một luật riêng.
/// </summary>
public sealed class TourGuideAccessSupport
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public TourGuideAccessSupport(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    /// <summary>
    /// Admin/Manager đi thẳng, không cần vé: họ phải mở được màn này để demo và để dò lỗi khi
    /// khách báo hỏng. Staff KHÔNG được — muốn thử thì đăng nhập tài khoản quản trị.
    /// </summary>
    public async Task<TourGuideAccess> EvaluateAsync(
        Guid? tripId,
        CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated || _userContext.UserId is not Guid userId)
        {
            return Denied(TourGuideAccessReasons.Unauthenticated, tripId);
        }

        if (!tripId.HasValue)
        {
            return Denied(TourGuideAccessReasons.NoTicket, null);
        }

        var user = await _context.Set<User>()
            .AsNoTracking()
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is not null && (AuthSupport.IsAdmin(user) || AuthSupport.IsManager(user)))
        {
            return new TourGuideAccess(true, TourGuideAccessReasons.Allowed, tripId, null, null);
        }

        // LỌC THEO TỪNG VÉ, KHÔNG THEO BOOKING. Booking khứ hồi có hai chặng trong cùng một
        // booking, nên hỏi "booking này có dính chuyến X không" sẽ trả về cả vé chiều kia —
        // và chiều đi đang CheckedIn sẽ mở luôn hướng dẫn viên cho chiều về, tức mở cửa cho
        // người còn chưa lên tàu. Chặng của vé nằm ở booking_passengers.trip_id.
        //
        // Vé không gắn hành khách (vé thuê tàu chưa điền tên) hoặc dữ liệu cũ chưa có trip_id
        // mới rơi về mức booking — những booking đó chỉ có một chuyến nên không lẫn được.
        var tickets = await _context.Set<Ticket>()
            .AsNoTracking()
            .Include(t => t.BookingPassenger)
            .Where(t => t.Booking.UserId == userId
                && ((t.BookingPassenger != null && t.BookingPassenger.TripId == tripId)
                    || ((t.BookingPassenger == null || t.BookingPassenger.TripId == null)
                        && (t.Booking.TripId == tripId
                            || t.Booking.CharterBoats.Any(cb => cb.TripId == tripId)))))
            .ToListAsync(cancellationToken);

        if (tickets.Count == 0)
        {
            return Denied(TourGuideAccessReasons.NoTicket, tripId);
        }

        var onBoard = tickets.FirstOrDefault(t =>
            t.TicketStatus == TicketStatus.CheckedIn && !t.CheckedOutAt.HasValue);

        if (onBoard is not null)
        {
            return new TourGuideAccess(
                true,
                TourGuideAccessReasons.Allowed,
                tripId,
                onBoard.CheckedInAt,
                await ResolveDisplayExpiryAsync(tripId.Value, onBoard, cancellationToken));
        }

        // Một chuyến vẫn có thể có nhiều vé của cùng tài khoản (đặt cho cả nhà) ở nhiều trạng
        // thái. Xếp theo mức "gần được dùng nhất" để câu báo nói đúng chuyện đang xảy ra.
        var reason = tickets.Any(t => t.TicketStatus == TicketStatus.CheckedOut)
            ? TourGuideAccessReasons.CheckedOut
            : tickets.Any(t => t.TicketStatus == TicketStatus.Active)
                ? TourGuideAccessReasons.NotCheckedIn
                : tickets.Any(t => t.TicketStatus == TicketStatus.Expired)
                    ? TourGuideAccessReasons.SessionExpired
                    : TourGuideAccessReasons.NoTicket;

        return Denied(reason, tripId);
    }

    /// <summary>
    /// Mốc hiện lên đồng hồ đếm ngược: đúng hạn check-out của vé tại bến khách xuống. Hỏng thì
    /// trả null — mất đồng hồ chứ không được làm hỏng lượt hỏi.
    /// </summary>
    private async Task<DateTimeOffset?> ResolveDisplayExpiryAsync(
        Guid tripId,
        Ticket ticket,
        CancellationToken cancellationToken)
    {
        var trip = await _context.Set<Trip>()
            .AsNoTracking()
            .Include(t => t.TripStops)
            .SingleOrDefaultAsync(t => t.Id == tripId, cancellationToken);

        return trip is null
            ? null
            : TicketAttendanceWindowSupport.ResolveCheckOutDeadline(
                trip,
                ticket.BookingPassenger?.ToStopOrder);
    }

    private static TourGuideAccess Denied(string reasonCode, Guid? tripId) =>
        new(false, reasonCode, tripId, null, null);
}
