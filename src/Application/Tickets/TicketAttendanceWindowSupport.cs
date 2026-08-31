using FluentValidation.Results;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Tickets;

public static class TicketAttendanceWindowSupport
{
    public const int CheckInLeadMinutes = 10;
    public const int CheckInCloseGraceMinutes = 2;
    public const int CheckOutOpenOffsetMinutes = 3;
    public const int CheckOutGraceMinutes = 5;

    /// <summary>
    /// Thời gian dừng GIẢ ĐỊNH cho bến khách xuống không khai thời gian dừng, chỉ dùng để tính
    /// hạn check-out tự động.
    ///
    /// Bến cuối LUÔN có StayDurationMinutes = 0 (xem TripStopScheduleSupport.ResolveStayDurationMinutes)
    /// và tàu thì không "rời" bến cuối, nên nếu không có mốc giả định này thì vé của khách quên
    /// check-out ở bến cuối treo CheckedIn vĩnh viễn — job dọn vé không bao giờ tính ra được hạn.
    /// Với sightseeing thì đó là MỌI tấm vé: hành khách sightseeing không lưu stop order nên bến
    /// xuống luôn rơi về bến cuối.
    /// </summary>
    public const int UnscheduledDwellFallbackMinutes = 10;

    public static void EnsureCanCheckInAt(Ticket ticket, DateTimeOffset now)
    {
        EnsureCanCheckInAt(ticket, ticket.Booking, now);
    }

    public static void EnsureCanCheckInAt(Ticket ticket, Booking booking, DateTimeOffset now)
    {
        var passenger = ResolveTicketPassenger(ticket, booking);
        var trip = ResolveTicketTrip(booking, passenger);
        if (trip is not null && trip.TripStops.Count > 0)
        {
            var boardingStop = ResolveBoardingStop(trip, passenger);
            if (boardingStop is null)
            {
                throw new ValidationException([new ValidationFailure("ticket",
                    "Không xác định được bến khách lên của vé, chưa thể check-in.")]);
            }

            EnsureStopOpenForScan(
                boardingStop,
                now,
                "Tàu chưa cập bến khách lên, chưa thể check-in.",
                "Đã quá 2 phút sau khi tàu rời bến khách lên, không thể check-in.");
            return;
        }

        if (!TryResolveSegmentTimes(booking, passenger, out var segmentTimes))
        {
            return;
        }

        var earliestCheckIn = segmentTimes.Departure.AddMinutes(-CheckInLeadMinutes);
        if (now < earliestCheckIn)
        {
            throw new ValidationException([new ValidationFailure("ticket",
                "Chỉ được check-in trong vòng 10 phút trước giờ tàu rời bến khách lên.")]);
        }

        if (now > segmentTimes.Departure.AddMinutes(CheckInCloseGraceMinutes))
        {
            throw new ValidationException([new ValidationFailure("ticket",
                $"Đã quá {CheckInCloseGraceMinutes} phút sau giờ tàu rời bến khách lên, không thể check-in.")]);
        }
    }

    public static void EnsureCanCheckOutAt(Ticket ticket, DateTimeOffset now)
    {
        EnsureCanCheckOutAt(ticket, ticket.Booking, now);
    }

    public static void EnsureCanCheckOutAt(Ticket ticket, Booking booking, DateTimeOffset now)
    {
        var passenger = ResolveTicketPassenger(ticket, booking);
        var trip = ResolveTicketTrip(booking, passenger);
        if (trip is not null && trip.TripStops.Count > 0)
        {
            var alightingStop = ResolveAlightingStop(trip, passenger);
            if (alightingStop is null)
            {
                throw new ValidationException([new ValidationFailure("ticket",
                    "Không xác định được bến khách xuống của vé, chưa thể check-out.")]);
            }

            EnsureStopOpenForCheckOut(
                alightingStop,
                now,
                "Tàu chưa cập bến khách xuống, chưa thể check-out.",
                "Tàu đã rời bến khách xuống, không thể check-out.",
                "Đã quá thời gian dừng tại bến khách xuống, không thể check-out.",
                CheckOutOpenOffsetMinutes);
            return;
        }

        if (!TryResolveSegmentTimes(booking, passenger, out var segmentTimes))
        {
            return;
        }

        var earliestCheckOut = segmentTimes.Arrival.AddMinutes(-CheckOutOpenOffsetMinutes);
        if (now < earliestCheckOut)
        {
            throw new ValidationException([new ValidationFailure("ticket",
                $"Chưa đến thời điểm mở check-out: phải trong vòng {CheckOutOpenOffsetMinutes} phút trước giờ tàu đến bến khách xuống.")]);
        }
        var latestCheckOut = trip is null
            ? segmentTimes.Arrival.AddMinutes(UnscheduledDwellFallbackMinutes + CheckOutGraceMinutes)
            : ResolveLatestCheckOutAt(trip, passenger, segmentTimes);
        if (now > latestCheckOut)
        {
            throw new ValidationException([new ValidationFailure("ticket",
                "Đã quá 5 phút sau giờ tàu rời bến khách xuống, không thể check-out.")]);
        }
    }

    public static bool IsWithinCheckInWindow(Ticket ticket, DateTimeOffset? now)
    {
        return IsWithinCheckInWindow(ticket, ticket.Booking, now);
    }

    public static bool IsWithinCheckInWindow(Ticket ticket, Booking booking, DateTimeOffset? now)
    {
        var passenger = ResolveTicketPassenger(ticket, booking);
        return IsWithinCheckInWindow(booking, passenger, now);
    }

    public static bool IsWithinCheckInWindow(Booking booking, BookingPassenger? passenger, DateTimeOffset? now)
    {
        var trip = ResolveTicketTrip(booking, passenger);
        if (trip is not null)
        {
            return IsWithinCheckInWindow(trip, passenger, now);
        }

        if (!now.HasValue || !TryResolveCharterSegmentTimes(booking, out var segmentTimes))
        {
            return true;
        }

        return IsWithinCheckInWindow(segmentTimes.Departure, now.Value);
    }

    public static bool IsWithinCheckInWindow(Trip? trip, BookingPassenger? passenger, DateTimeOffset? now)
    {
        if (!now.HasValue || trip is null)
        {
            return true;
        }

        if (trip.TripStops.Count > 0)
        {
            var boardingStop = ResolveBoardingStop(trip, passenger);
            return boardingStop is not null && IsStopOpenForScan(boardingStop, now.Value);
        }

        var segmentTimes = TripStopScheduleSupport.ResolveSegmentTimes(
            trip,
            passenger?.FromStopOrder,
            passenger?.ToStopOrder);
        return IsWithinCheckInWindow(segmentTimes.Departure, now.Value);
    }

    public static bool IsWithinCheckOutWindow(Ticket ticket, DateTimeOffset? now)
    {
        return IsWithinCheckOutWindow(ticket, ticket.Booking, now);
    }

    public static bool IsWithinCheckOutWindow(Ticket ticket, Booking booking, DateTimeOffset? now)
    {
        if (!now.HasValue)
        {
            return true;
        }

        var passenger = ResolveTicketPassenger(ticket, booking);
        return IsWithinCheckOutWindow(booking, passenger, now);
    }

    public static bool IsWithinCheckOutWindow(Booking booking, BookingPassenger? passenger, DateTimeOffset? now)
    {
        if (!now.HasValue)
        {
            return true;
        }

        var trip = ResolveTicketTrip(booking, passenger);
        if (trip is null)
        {
            if (!TryResolveCharterSegmentTimes(booking, out var charterTimes))
            {
                return true;
            }

            var charterEarliestCheckOut = charterTimes.Arrival.AddMinutes(-CheckOutOpenOffsetMinutes);
            var charterLatestCheckOut = charterTimes.Arrival
                .AddMinutes(UnscheduledDwellFallbackMinutes + CheckOutGraceMinutes);
            return now.Value >= charterEarliestCheckOut && now.Value <= charterLatestCheckOut;
        }

        if (trip.TripStops.Count > 0)
        {
            var alightingStop = ResolveAlightingStop(trip, passenger);
            if (alightingStop is null)
            {
                return false;
            }
            return IsStopOpenForCheckOut(alightingStop, now.Value);
        }

        var segmentTimes = TripStopScheduleSupport.ResolveSegmentTimes(
            trip,
            passenger?.FromStopOrder,
            passenger?.ToStopOrder);
        var earliestCheckOut = segmentTimes.Arrival.AddMinutes(-CheckOutOpenOffsetMinutes);
        var latestCheckOut = ResolveLatestCheckOutAt(trip, passenger, segmentTimes);
        return now.Value >= earliestCheckOut && now.Value <= latestCheckOut;
    }

    /// <summary>
    /// Vé này đã QUA hạn check-in chưa — xét theo đúng chặng của chính nó.
    ///
    /// CỐ Ý TÁCH KHỎI <see cref="IsWithinCheckInWindow(Ticket, Booking, DateTimeOffset?)"/>: hàm
    /// kia trả false cho CẢ "chưa tới lượt" lẫn "đã qua rồi", nên <c>!IsWithinCheckInWindow(...)</c>
    /// KHÔNG có nghĩa là hết hạn. Job dọn vé từng hiểu nhầm đúng chỗ này và giết sạch vé khứ hồi
    /// ngay sau khi khách thanh toán.
    ///
    /// Không xác định được chuyến hoặc bến thì trả false: thà để vé sống thừa còn hơn huỷ nhầm vé
    /// khách đã trả tiền.
    /// </summary>
    public static bool IsPastCheckInWindow(Ticket ticket, Booking booking, DateTimeOffset now)
    {
        var passenger = ResolveTicketPassenger(ticket, booking);
        var trip = ResolveTicketOwnTrip(booking, passenger);
        if (trip is null)
        {
            return TryResolveCharterSegmentTimes(booking, out var charterTimes)
                && now > charterTimes.Departure.AddMinutes(CheckInCloseGraceMinutes);
        }

        if (trip.TripStops.Count > 0)
        {
            var boardingStop = ResolveBoardingStop(trip, passenger);
            return boardingStop is not null && IsStopPastScanWindow(boardingStop, now);
        }

        var segmentTimes = TripStopScheduleSupport.ResolveSegmentTimes(
            trip,
            passenger?.FromStopOrder,
            passenger?.ToStopOrder);
        return now > segmentTimes.Departure.AddMinutes(CheckInCloseGraceMinutes);
    }

    /// <summary>
    /// Vé này đã QUA hạn check-out chưa. Xem ghi chú ở <see cref="IsPastCheckInWindow"/> về lý do
    /// không dùng phủ định của <see cref="IsWithinCheckOutWindow(Ticket, Booking, DateTimeOffset?)"/>.
    /// </summary>
    public static bool IsPastCheckOutWindow(Ticket ticket, Booking booking, DateTimeOffset now)
    {
        var passenger = ResolveTicketPassenger(ticket, booking);
        var trip = ResolveTicketOwnTrip(booking, passenger);
        if (trip is null)
        {
            return TryResolveCharterSegmentTimes(booking, out var charterTimes)
                && now > charterTimes.Arrival.AddMinutes(
                    UnscheduledDwellFallbackMinutes + CheckOutGraceMinutes);
        }

        if (trip.TripStops.Count > 0)
        {
            var alightingStop = ResolveAlightingStop(trip, passenger);
            return alightingStop is not null && IsStopPastCheckOutWindow(alightingStop, now);
        }

        var segmentTimes = TripStopScheduleSupport.ResolveSegmentTimes(
            trip,
            passenger?.FromStopOrder,
            passenger?.ToStopOrder);
        return now > ResolveLatestCheckOutAt(trip, passenger, segmentTimes);
    }

    /// <summary>
    /// Chuyến của CHÍNH tấm vé này. Khác <see cref="ResolveTicketTrip"/> ở chỗ không đoán bừa:
    /// hành khách ghi rõ một chặng lạ thì trả null thay vì rơi về chuyến chiều đi, và hành khách
    /// rỗng không lọt vào bẫy `null == null` của hàm kia.
    /// </summary>
    private static Trip? ResolveTicketOwnTrip(Booking booking, BookingPassenger? passenger)
    {
        if (passenger?.Trip is not null)
        {
            return passenger.Trip;
        }

        if (passenger?.TripId is Guid tripId)
        {
            if (tripId == booking.ReturnTripId)
            {
                return booking.ReturnTrip;
            }

            return tripId == booking.TripId ? booking.Trip : null;
        }

        // Vé không gắn hành khách (vé charter chưa điền tên): cả booking chỉ có một chuyến.
        return booking.Trip;
    }

    /// <summary>
    /// Bến này đã ĐÓNG cửa check-in chưa: bến bị bỏ qua hoặc đã quá 2 phút sau khi tàu rời bến.
    /// Chú ý: bến CHƯA cập trả false ở đây, và cũng trả false ở <see cref="IsStopOpenForScan"/> —
    /// hai hàm không phải phủ định của nhau.
    /// </summary>
    private static bool IsStopPastScanWindow(TripStop stop, DateTimeOffset now)
    {
        if (string.Equals(stop.StopStatus, TripStopStatuses.Skipped, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var closesAt = ResolveCheckInClosesAt(stop);
        if (closesAt.HasValue)
        {
            return now > closesAt.Value;
        }

        return string.Equals(stop.StopStatus, TripStopStatuses.Departed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Bến khách XUỐNG đã hết hạn check-out chưa.
    ///
    /// CỐ Ý TÁCH KHỎI <see cref="IsStopPastScanWindow"/> dù hai hàm nhìn gần giống nhau: hàm kia
    /// còn phục vụ đường check-IN, mà bến ĐẦU cũng luôn có StayDurationMinutes = 0. Gắn mốc giả
    /// định vào đó thì tàu đỗ bến đầu lâu hơn nửa tiếng (khởi hành trễ) là giết sạch vé của khách
    /// còn chưa kịp lên — đúng loại tai nạn mà ghi chú ở <see cref="IsPastCheckInWindow"/> cảnh báo.
    /// Ở đây chỉ đụng vé ĐÃ check-in, tức khách đã đi xong: lỡ tay thì mất quyền check-out chứ
    /// không mất vé.
    /// </summary>
    private static bool IsStopPastCheckOutWindow(TripStop stop, DateTimeOffset now)
    {
        if (stop.ActualDepartureTime.HasValue
            || string.Equals(stop.StopStatus, TripStopStatuses.Departed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(stop.StopStatus, TripStopStatuses.Skipped, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var deadline = ResolveCheckOutDeadline(stop);
        return deadline.HasValue && now > deadline.Value;
    }

    /// <summary>
    /// Hạn chót check-out của một chặng, tra theo bến khách xuống của chuyến. Hành khách không
    /// ghi chặng (sightseeing chiếm ghế cả vòng) thì lấy bến cuối.
    ///
    /// CỐ Ý NHẬN THẲNG trip + stop order thay vì nhận Ticket: người gọi ngoài luồng quét vé
    /// (hướng dẫn viên AI) đã biết chính xác chuyến khách đang đi, không cần và không nên đi qua
    /// <c>ResolveTicketTrip</c> — hàm đó đoán chuyến từ booking và trả null cho vé thuê tàu.
    ///
    /// Trả null khi chuyến chưa có lịch từng bến: không đoán được thì đừng bịa ra một cái mốc.
    /// </summary>
    public static DateTimeOffset? ResolveCheckOutDeadline(Trip trip, int? toStopOrder)
    {
        if (trip.TripStops.Count == 0)
        {
            return null;
        }

        var orderedStops = trip.TripStops.OrderBy(x => x.StopOrder).ToArray();
        var alightingStop = toStopOrder.HasValue
            ? orderedStops.FirstOrDefault(x => x.StopOrder == toStopOrder.Value)
            : orderedStops.LastOrDefault();

        return alightingStop is null ? null : ResolveCheckOutDeadline(alightingStop);
    }

    /// <summary>
    /// Hạn chót check-out tại một bến = giờ tàu đến + thời gian dừng + ân hạn.
    ///
    /// Giờ đến ưu tiên THỰC TẾ → ĐIỀU CHỈNH → DỰ KIẾN: chuyến trễ có ghi nhận thì
    /// <see cref="TripStop.AdjustedArrivalTime"/> đã cộng sẵn phút trễ, lấy giờ dự kiến trần trụi
    /// sẽ huỷ quyền check-out của khách còn đang ngồi trên tàu. Chấp nhận rơi về giờ dự kiến khi
    /// bến chưa hề được bấm cập: thà đóng muộn hơn 15 phút còn hơn treo vĩnh viễn.
    ///
    /// Trả null khi bến không có bất kỳ mốc giờ nào — không đoán được thì để vé sống.
    /// </summary>
    private static DateTimeOffset? ResolveCheckOutDeadline(TripStop stop)
    {
        var arrival = stop.ActualArrivalTime ?? stop.AdjustedArrivalTime ?? stop.PlannedArrivalTime;
        if (!arrival.HasValue)
        {
            return null;
        }

        var dwellMinutes = stop.StayDurationMinutes > 0
            ? stop.StayDurationMinutes
            : UnscheduledDwellFallbackMinutes;

        return arrival.Value.AddMinutes(dwellMinutes + CheckOutGraceMinutes);
    }

    private static bool TryResolveSegmentTimes(
        Ticket ticket,
        Booking booking,
        out (DateTimeOffset Departure, DateTimeOffset Arrival) segmentTimes)
    {
        var passenger = ResolveTicketPassenger(ticket, booking);
        return TryResolveSegmentTimes(booking, passenger, out segmentTimes);
    }

    private static bool TryResolveSegmentTimes(
        Booking booking,
        BookingPassenger? passenger,
        out (DateTimeOffset Departure, DateTimeOffset Arrival) segmentTimes)
    {
        var trip = ResolveTicketTrip(booking, passenger);
        if (trip is null)
        {
            return TryResolveCharterSegmentTimes(booking, out segmentTimes);
        }

        segmentTimes = TripStopScheduleSupport.ResolveSegmentTimes(
            trip,
            passenger?.FromStopOrder,
            passenger?.ToStopOrder);
        return true;
    }

    private static bool TryResolveCharterSegmentTimes(
        Booking booking,
        out (DateTimeOffset Departure, DateTimeOffset Arrival) segmentTimes)
    {
        if (!Booking.IsCharterBookingType(booking.BookingType) || !booking.DepartureDate.HasValue)
        {
            segmentTimes = default;
            return false;
        }

        segmentTimes = CharterBookingTripSupport.ResolveRentalWindowUtc(booking);
        return true;
    }

    private static bool IsWithinCheckInWindow(DateTimeOffset departure, DateTimeOffset now) =>
        now >= departure.AddMinutes(-CheckInLeadMinutes)
        && now <= departure.AddMinutes(CheckInCloseGraceMinutes);

    private static BookingPassenger? ResolveTicketPassenger(Ticket ticket, Booking booking)
    {
        if (ticket.BookingPassenger is not null)
        {
            return ticket.BookingPassenger;
        }

        return ticket.BookingPassengerId.HasValue
            ? booking.Passengers.FirstOrDefault(x => x.Id == ticket.BookingPassengerId.Value)
            : null;
    }

    private static Trip? ResolveTicketTrip(Booking booking, BookingPassenger? passenger)
    {
        if (passenger?.Trip is not null)
        {
            return passenger.Trip;
        }

        if (passenger?.TripId == booking.ReturnTripId)
        {
            return booking.ReturnTrip;
        }

        if (passenger?.TripId == booking.TripId)
        {
            return booking.Trip;
        }

        return booking.Trip;
    }

    private static TripStop? ResolveBoardingStop(Trip trip, BookingPassenger? passenger)
    {
        var orderedStops = trip.TripStops
            .OrderBy(x => x.StopOrder)
            .ToArray();

        if (passenger?.FromStopOrder is int fromStopOrder)
        {
            return orderedStops.FirstOrDefault(x => x.StopOrder == fromStopOrder);
        }

        if (passenger?.FromStationId is Guid fromStationId)
        {
            return orderedStops.FirstOrDefault(x => x.StationId == fromStationId);
        }

        return orderedStops.FirstOrDefault();
    }

    private static TripStop? ResolveAlightingStop(Trip trip, BookingPassenger? passenger)
    {
        var orderedStops = trip.TripStops
            .OrderBy(x => x.StopOrder)
            .ToArray();

        if (passenger?.ToStopOrder is int toStopOrder)
        {
            return orderedStops.FirstOrDefault(x => x.StopOrder == toStopOrder);
        }

        if (passenger?.ToStationId is Guid toStationId)
        {
            return orderedStops.FirstOrDefault(x => x.StationId == toStationId);
        }

        return orderedStops.LastOrDefault();
    }

    private static DateTimeOffset ResolveLatestCheckOutAt(
        Trip trip,
        BookingPassenger? passenger,
        (DateTimeOffset Departure, DateTimeOffset Arrival) segmentTimes)
    {
        var dwellMinutes = ResolveDwellMinutes(trip, passenger);
        DateTimeOffset effectiveDeparture;
        if (dwellMinutes.HasValue)
        {
            effectiveDeparture = segmentTimes.Arrival.AddMinutes(dwellMinutes.Value);
        }
        else if (trip.TripStops.Count > 0)
        {
            effectiveDeparture = segmentTimes.Departure;
        }
        else
        {
            effectiveDeparture = segmentTimes.Arrival.AddMinutes(UnscheduledDwellFallbackMinutes);
        }
        return effectiveDeparture.AddMinutes(CheckOutGraceMinutes);
    }

    private static int? ResolveDwellMinutes(Trip trip, BookingPassenger? passenger)
    {
        if (trip.TripStops.Count == 0)
        {
            return null;
        }

        var toStopOrder = passenger?.ToStopOrder;

        var alightingStop = trip.TripStops
            .OrderBy(x => x.StopOrder)
            .FirstOrDefault(x => x.StopOrder == toStopOrder
                || (toStopOrder is null && x.StopOrder == trip.TripStops.Count));

        return alightingStop?.StayDurationMinutes is > 0
            ? alightingStop.StayDurationMinutes
            : null;
    }

    private static void EnsureStopOpenForScan(
        TripStop stop,
        DateTimeOffset now,
        string notArrivedMessage,
        string departedMessage)
    {
        if (string.Equals(stop.StopStatus, TripStopStatuses.Skipped, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure("ticket", departedMessage)]);
        }

        var closesAt = ResolveCheckInClosesAt(stop);
        if (closesAt.HasValue && now > closesAt.Value)
        {
            throw new ValidationException([new ValidationFailure("ticket", departedMessage)]);
        }

        var hasReachedStop = (string.Equals(
                stop.StopStatus,
                TripStopStatuses.Arrived,
                StringComparison.OrdinalIgnoreCase)
                && stop.ActualArrivalTime.HasValue)
            || stop.ActualDepartureTime.HasValue
            || string.Equals(stop.StopStatus, TripStopStatuses.Departed, StringComparison.OrdinalIgnoreCase);
        if (!hasReachedStop)
        {
            throw new ValidationException([new ValidationFailure("ticket", notArrivedMessage)]);
        }

        var expectedDeparture = ResolveExpectedStopDeparture(stop);
        if (expectedDeparture.HasValue
            && now < expectedDeparture.Value.AddMinutes(-CheckInLeadMinutes))
        {
            throw new ValidationException([new ValidationFailure("ticket",
                $"Chỉ được check-in trong vòng {CheckInLeadMinutes} phút trước giờ tàu rời bến khách lên.")]);
        }

    }

    private static void EnsureStopOpenForCheckOut(
        TripStop stop,
        DateTimeOffset now,
        string notArrivedMessage,
        string departedMessage,
        string dwellExpiredMessage,
        int openOffsetMinutes)
    {
        if (stop.ActualDepartureTime.HasValue
            || string.Equals(stop.StopStatus, TripStopStatuses.Departed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(stop.StopStatus, TripStopStatuses.Skipped, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure("ticket", departedMessage)]);
        }

        if (!string.Equals(stop.StopStatus, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase)
            || !stop.ActualArrivalTime.HasValue)
        {
            var expectedArrival = stop.AdjustedArrivalTime ?? stop.PlannedArrivalTime;
            if (!expectedArrival.HasValue
                || now < expectedArrival.Value.AddMinutes(-openOffsetMinutes))
            {
                throw new ValidationException([new ValidationFailure("ticket",
                    $"Chưa đến thời điểm mở check-out: phải trong vòng {openOffsetMinutes} phút trước giờ tàu đến bến khách xuống.")]);
            }

            var deadline = ResolveCheckOutDeadline(stop);
            if (deadline.HasValue && now > deadline.Value)
            {
                throw new ValidationException([new ValidationFailure("ticket", dwellExpiredMessage)]);
            }

            return;
        }

        var stopScanEndsAt = ResolveStopScanEndsAt(stop);
        var effectiveEnd = stopScanEndsAt?.AddMinutes(CheckOutGraceMinutes);
        if (effectiveEnd.HasValue && now > effectiveEnd.Value)
        {
            throw new ValidationException([new ValidationFailure("ticket", dwellExpiredMessage)]);
        }
    }

    private static bool IsStopOpenForScan(TripStop stop, DateTimeOffset now)
    {
        if (string.Equals(stop.StopStatus, TripStopStatuses.Skipped, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hasReachedStop = (string.Equals(
                stop.StopStatus,
                TripStopStatuses.Arrived,
                StringComparison.OrdinalIgnoreCase)
                && stop.ActualArrivalTime.HasValue)
            || stop.ActualDepartureTime.HasValue
            || string.Equals(stop.StopStatus, TripStopStatuses.Departed, StringComparison.OrdinalIgnoreCase);
        if (!hasReachedStop)
        {
            return false;
        }

        var expectedDeparture = ResolveExpectedStopDeparture(stop);
        if (expectedDeparture.HasValue
            && now < expectedDeparture.Value.AddMinutes(-CheckInLeadMinutes))
        {
            return false;
        }

        var closesAt = ResolveCheckInClosesAt(stop);
        return !closesAt.HasValue || now <= closesAt.Value;
    }

    private static bool IsStopOpenForCheckOut(TripStop stop, DateTimeOffset now)
    {
        if (stop.ActualDepartureTime.HasValue
            || string.Equals(stop.StopStatus, TripStopStatuses.Departed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(stop.StopStatus, TripStopStatuses.Skipped, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(stop.StopStatus, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase)
            || !stop.ActualArrivalTime.HasValue)
        {
            var expectedArrival = stop.AdjustedArrivalTime ?? stop.PlannedArrivalTime;
            var deadline = ResolveCheckOutDeadline(stop);
            return expectedArrival.HasValue
                && now >= expectedArrival.Value.AddMinutes(-CheckOutOpenOffsetMinutes)
                && (!deadline.HasValue || now <= deadline.Value);
        }

        var stopScanEndsAt = ResolveStopScanEndsAt(stop);
        var effectiveEnd = stopScanEndsAt?.AddMinutes(CheckOutGraceMinutes);
        return !effectiveEnd.HasValue || now <= effectiveEnd.Value;
    }

    private static DateTimeOffset? ResolveStopScanEndsAt(TripStop stop)
    {
        if (stop.ActualArrivalTime.HasValue)
        {
            var dwellMinutes = stop.StayDurationMinutes > 0
                ? stop.StayDurationMinutes
                : UnscheduledDwellFallbackMinutes;
            return stop.ActualArrivalTime.Value.AddMinutes(dwellMinutes);
        }

        return null;
    }

    private static DateTimeOffset? ResolveExpectedStopDeparture(TripStop stop) =>
        stop.AdjustedDepartureTime
        ?? stop.PlannedDepartureTime
        ?? (stop.ActualArrivalTime.HasValue && stop.StayDurationMinutes > 0
            ? stop.ActualArrivalTime.Value.AddMinutes(stop.StayDurationMinutes)
            : null);

    private static DateTimeOffset? ResolveCheckInClosesAt(TripStop stop)
    {
        if (stop.ActualDepartureTime.HasValue)
        {
            return stop.ActualDepartureTime.Value.AddMinutes(CheckInCloseGraceMinutes);
        }

        return string.Equals(stop.StopStatus, TripStopStatuses.Departed, StringComparison.OrdinalIgnoreCase)
            ? ResolveExpectedStopDeparture(stop)?.AddMinutes(CheckInCloseGraceMinutes)
            : null;
    }
}
