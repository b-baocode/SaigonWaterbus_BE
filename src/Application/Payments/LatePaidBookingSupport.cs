using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Payments;

/// <summary>
/// PayOS báo "đã thanh toán" SAU khi booking đã bị đánh Expired/Cancelled — tiền về trễ hơn hạn giữ
/// chỗ (khách bấm chuyển khoản sát hạn, ngân hàng xử lý chậm).
///
/// Đổi bookings.status sang Confirmed chính là hành động CHIẾM GHẾ (xem BookingSeatOccupancySupport),
/// nên trước khi hồi sinh phải kiểm lại đúng những gì hai đường tạo booking đã kiểm: ghế còn trống và
/// tàu chưa rời bến khách lên. Nếu không thì tiền vẫn ghi nhận nhưng booking giữ nguyên trạng thái
/// chết, và mở sẵn yêu cầu hoàn tiền thay vì phát vé cho một chỗ đã có người khác ngồi.
/// </summary>
public static class LatePaidBookingSupport
{
    /// <summary>Kết quả kiểm tra: có được phép hồi sinh booking không, không thì vì lý do gì.</summary>
    public sealed record Decision(bool CanConfirm, string? BlockReason);

    private static readonly Decision Allowed = new(true, null);

    /// <summary>Booking vé thường đã chết — chỉ những booking này mới cần kiểm tra hồi sinh.</summary>
    public static bool IsDeadRegularBooking(Booking booking) =>
        !Booking.IsCharterBookingType(booking.BookingType)
        && booking.BookingStatus is BookingStatus.Expired or BookingStatus.Cancelled;

    public static async Task<Decision> EvaluateAsync(
        IApplicationDbContext context,
        Booking booking,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!IsDeadRegularBooking(booking))
        {
            return Allowed;
        }

        var passengers = await context.Set<BookingPassenger>()
            .AsNoTracking()
            .Where(x => x.BookingId == booking.Id)
            .Select(x => new PassengerSeat(
                x.Id,
                x.TripId ?? booking.TripId,
                x.TripSeatId,
                x.FromStopOrder,
                x.ToStopOrder))
            .ToListAsync(cancellationToken);

        if (passengers.Count == 0)
        {
            return Allowed;
        }

        var departedTripCode = await FindDepartedTripCodeAsync(context, passengers, now, cancellationToken);
        if (departedTripCode is not null)
        {
            return new Decision(false,
                $"Tàu của chuyến {departedTripCode} đã rời bến khách lên trước khi thanh toán được ghi nhận.");
        }

        var takenSeatCode = await FindSeatTakenByOthersAsync(context, booking.Id, passengers, now, cancellationToken);
        if (takenSeatCode is not null)
        {
            return new Decision(false,
                $"Ghế {takenSeatCode} đã được bán cho khách khác trước khi thanh toán được ghi nhận.");
        }

        return Allowed;
    }

    /// <summary>
    /// Ghi nhận tiền đã thu nhưng không phát vé: mở yêu cầu hoàn tiền để admin xử lý và báo khách.
    /// Trả về notification vừa tạo (rỗng nếu booking là khách vãng lai không có tài khoản).
    /// </summary>
    public static IReadOnlyList<Notification> MarkForRefund(
        IApplicationDbContext context,
        Booking booking,
        Payment payment,
        string reason,
        DateTimeOffset now)
    {
        payment.RefundStatus = PaymentSupport.RefundPendingStatus;
        payment.RefundRequestedAmount = payment.Amount;
        payment.RefundMethod = PaymentSupport.PayOsProvider;
        payment.RefundReason = reason;
        payment.RefundReferenceId ??= PaymentSupport.CreateRefundReference(payment, now);
        payment.RefundFailureReason = null;

        if (!booking.UserId.HasValue)
        {
            return [];
        }

        var notification = new Notification
        {
            UserId = booking.UserId.Value,
            Title = "Thanh toán về trễ — sẽ hoàn tiền",
            Body = $"Booking {booking.BookingCode}: {reason} Chúng tôi đã ghi nhận khoản tiền và sẽ hoàn lại cho bạn.",
            Type = NotificationTypes.BookingPaymentRefundPending,
            RelatedEntityType = NotificationRelatedEntityTypes.Booking,
            RelatedEntityId = booking.Id,
            CreatedAt = now
        };
        context.Set<Notification>().Add(notification);
        return [notification];
    }

    private static async Task<string?> FindDepartedTripCodeAsync(
        IApplicationDbContext context,
        IReadOnlyList<PassengerSeat> passengers,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var tripIds = passengers
            .Where(x => x.TripId.HasValue)
            .Select(x => x.TripId!.Value)
            .Distinct()
            .ToList();
        if (tripIds.Count == 0)
        {
            return null;
        }

        var trips = await context.Set<Trip>()
            .AsNoTracking()
            .Include(x => x.TripStops)
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
            .Where(x => tripIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        foreach (var passenger in passengers.Where(x => x.TripId.HasValue))
        {
            var trip = trips.FirstOrDefault(x => x.Id == passenger.TripId!.Value);
            if (trip is null)
            {
                continue;
            }

            if (BookingCutoffSupport.IsPastBoarding(
                    trip, passenger.FromStopOrder, passenger.ToStopOrder, now))
            {
                return trip.TripCode;
            }
        }

        return null;
    }

    private static async Task<string?> FindSeatTakenByOthersAsync(
        IApplicationDbContext context,
        Guid bookingId,
        IReadOnlyList<PassengerSeat> passengers,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var tripSeatIds = passengers
            .Where(x => x.TripSeatId.HasValue)
            .Select(x => x.TripSeatId!.Value)
            .Distinct()
            .ToList();
        if (tripSeatIds.Count == 0)
        {
            return null;
        }

        var others = await context.Set<BookingPassenger>()
            .AsNoTracking()
            .Where(x => x.BookingId != bookingId
                     && x.TripSeatId.HasValue
                     && tripSeatIds.Contains(x.TripSeatId.Value))
            .Where(BookingSeatOccupancySupport.PassengerOccupiesSeat(now))
            .Select(x => new
            {
                TripSeatId = x.TripSeatId!.Value,
                x.FromStopOrder,
                x.ToStopOrder,
                SeatCode = x.TripSeat!.Seat.Code
            })
            .ToListAsync(cancellationToken);

        foreach (var passenger in passengers.Where(x => x.TripSeatId.HasValue))
        {
            var conflict = others.FirstOrDefault(x =>
                x.TripSeatId == passenger.TripSeatId!.Value
                && BookingSeatOccupancySupport.SegmentsOverlap(
                    x.FromStopOrder ?? BookingSeatOccupancySupport.FullTripFromOrder,
                    x.ToStopOrder ?? BookingSeatOccupancySupport.FullTripToOrder,
                    passenger.FromStopOrder ?? BookingSeatOccupancySupport.FullTripFromOrder,
                    passenger.ToStopOrder ?? BookingSeatOccupancySupport.FullTripToOrder));
            if (conflict is not null)
            {
                return conflict.SeatCode;
            }
        }

        return null;
    }

    private sealed record PassengerSeat(
        Guid Id,
        Guid? TripId,
        Guid? TripSeatId,
        int? FromStopOrder,
        int? ToStopOrder);
}
