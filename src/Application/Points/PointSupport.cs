using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Points;

public static class PointTransactionTypes
{
    /// <summary>Tích điểm 1% sau khi khách đã sử dụng xong dịch vụ (chuyến hoàn tất).</summary>
    public const string Earn = "Earn";

    /// <summary>Trừ điểm khi khách dùng point thanh toán lúc checkout.</summary>
    public const string Redeem = "Redeem";

    /// <summary>Hoàn lại điểm Redeem do khách đổi số point ngay tại màn checkout.</summary>
    public const string RedeemCancelled = "RedeemCancelled";

    /// <summary>Hoàn lại điểm Redeem do booking hết hạn/hủy/hoàn tiền.</summary>
    public const string RedeemReturned = "RedeemReturned";

    /// <summary>Thu hồi điểm Earn do booking được hoàn tiền.</summary>
    public const string EarnRevoked = "EarnRevoked";
}

/// <summary>
/// Mọi thay đổi PointBalance đều đi qua đây để luôn kèm một dòng point_transactions
/// (sổ cái) trong cùng SaveChanges với caller. 1 point = 1 VND.
/// </summary>
public static class PointSupport
{
    /// <summary>Tỷ lệ tích điểm: 1% số tiền thực trả của booking.</summary>
    public const decimal EarnRate = 0.01m;

    private const string PaidPaymentStatus = "Paid";

    /// <summary>Point chỉ được trả tối đa 50% bill — phần còn lại luôn qua PayOS (PayOS không nhận đơn 0 đồng).</summary>
    public const decimal MaxRedeemShareOfBill = 0.5m;

    public static int CalculateEarnedPoints(decimal paidAmount) =>
        paidAmount <= 0 ? 0 : (int)Math.Floor(paidAmount * EarnRate);

    public static int CalculateMaxRedeemablePoints(decimal billAmount) =>
        billAmount <= 0 ? 0 : (int)Math.Floor(billAmount * MaxRedeemShareOfBill);

    /// <summary>
    /// Cộng/trừ balance và ghi sổ. <paramref name="points"/> dương là cộng, âm là trừ.
    /// Caller chịu trách nhiệm SaveChanges.
    /// </summary>
    public static PointTransaction AddTransaction(
        IApplicationDbContext context,
        User user,
        Guid? bookingId,
        string transactionType,
        int points,
        string? description,
        DateTimeOffset now)
    {
        user.PointBalance += points;
        var transaction = new PointTransaction
        {
            UserId = user.Id,
            BookingId = bookingId,
            TransactionType = transactionType,
            Points = points,
            BalanceAfter = user.PointBalance,
            Description = description,
            CreatedAt = now
        };
        context.Set<PointTransaction>().Add(transaction);
        return transaction;
    }

    /// <summary>
    /// Tích điểm cho booking SAU KHI khách đã dùng xong dịch vụ (chuyến Completed / charter Completed).
    /// Cố tình KHÔNG tích lúc thanh toán: nếu tích ngay lúc trả tiền thì khách có thể tiêu điểm đó
    /// cho đơn khác rồi quay lại hủy/hoàn tiền đơn gốc — điểm đã tiêu không thu hồi đủ được.
    /// Căn cứ tích là số tiền THỰC TRẢ (payment Paid trừ phần đã hoàn), nên phần trả bằng point
    /// và phần đã refund đều không được tính. Idempotent theo booking nhờ guard PointsEarned.
    /// Caller chịu trách nhiệm SaveChanges.
    /// </summary>
    public static async Task AwardCompletionPointsAsync(
        IApplicationDbContext context,
        Booking booking,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!booking.UserId.HasValue || booking.PointsEarned > 0)
        {
            return;
        }

        var netPaidAmount = await context.Set<Payment>()
            .Where(p => p.BookingId == booking.Id && p.PaymentStatus == PaidPaymentStatus)
            .SumAsync(p => (decimal?)(p.Amount - p.RefundAmount), cancellationToken) ?? 0m;

        var earnedPoints = CalculateEarnedPoints(netPaidAmount);
        if (earnedPoints <= 0)
        {
            return;
        }

        var user = await context.Set<User>()
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == booking.UserId.Value, cancellationToken);
        if (user is null || !AuthSupport.IsCustomer(user))
        {
            return;
        }

        AddTransaction(
            context,
            user,
            booking.Id,
            PointTransactionTypes.Earn,
            earnedPoints,
            $"Tích điểm 1% sau khi hoàn tất dịch vụ booking {booking.BookingCode}",
            now);
        booking.PointsEarned += earnedPoints;
    }

    /// <summary>
    /// Chuyến vừa chuyển sang Completed → tích điểm cho các booking thường trên chuyến.
    /// Booking khứ hồi chỉ được tích khi CẢ hai chiều đã Completed (dịch vụ mới thật sự xong).
    /// Charter không đi đường này — charter tích khi booking chuyển Completed.
    /// Caller chịu trách nhiệm SaveChanges.
    /// </summary>
    public static async Task AwardCompletionPointsForCompletedTripAsync(
        IApplicationDbContext context,
        Trip trip,
        TripStatus oldStatus,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (trip.TripStatus == oldStatus || trip.TripStatus != TripStatus.Completed)
        {
            return;
        }

        var bookings = await context.Set<Booking>()
            .Where(b => b.UserId != null
                && b.PointsEarned == 0
                && b.BookingType == Booking.SeatBookingType
                && (b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Completed)
                && (b.TripId == trip.Id
                    || b.ReturnTripId == trip.Id
                    || b.Passengers.Any(p => p.TripId == trip.Id)))
            .ToListAsync(cancellationToken);
        if (bookings.Count == 0)
        {
            return;
        }

        // Chiều còn lại của booking khứ hồi có thể chưa chạy xong — chỉ tích khi mọi chiều đã Completed.
        var otherTripIds = bookings
            .SelectMany(b => new[] { b.TripId, b.ReturnTripId })
            .Where(id => id.HasValue && id.Value != trip.Id)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        List<Guid> completedOtherTripIds = otherTripIds.Count == 0
            ? []
            : await context.Set<Trip>()
                .Where(t => otherTripIds.Contains(t.Id) && t.TripStatus == TripStatus.Completed)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

        foreach (var booking in bookings)
        {
            var allLegsCompleted = new[] { booking.TripId, booking.ReturnTripId }
                .Where(id => id.HasValue)
                .All(id => id!.Value == trip.Id || completedOtherTripIds.Contains(id.Value));
            if (!allLegsCompleted)
            {
                continue;
            }

            await AwardCompletionPointsAsync(context, booking, now, cancellationToken);
        }
    }

    /// <summary>
    /// Hoàn lại point đã dùng khi booking chết (hết hạn giữ chỗ, bị hủy, hoàn tiền đủ)
    /// rồi reset PointsUsed về 0 — nhờ đó gọi lặp lại là no-op và booking có thể
    /// redeem lại từ đầu nếu quay về trạng thái thanh toán được. Caller SaveChanges.
    /// </summary>
    public static async Task ReturnRedeemedPointsAsync(
        IApplicationDbContext context,
        Booking booking,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (booking.PointsUsed <= 0 || !booking.UserId.HasValue)
        {
            return;
        }

        var user = await context.Set<User>()
            .SingleOrDefaultAsync(u => u.Id == booking.UserId.Value, cancellationToken);
        if (user is null)
        {
            return;
        }

        AddTransaction(
            context,
            user,
            booking.Id,
            PointTransactionTypes.RedeemReturned,
            booking.PointsUsed,
            reason,
            now);
        booking.PointsUsed = 0;
    }

    /// <summary>
    /// Điều chỉnh point khi booking được hoàn tiền: thu hồi điểm đã tích (mọi mức refund)
    /// và trả lại điểm đã dùng khi booking hoàn tiền đủ (BookingStatus.Refunded).
    /// Idempotent theo booking. Caller SaveChanges.
    /// </summary>
    public static async Task ApplyRefundPointAdjustmentsAsync(
        IApplicationDbContext context,
        Booking booking,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!booking.UserId.HasValue)
        {
            return;
        }

        var user = await context.Set<User>()
            .SingleOrDefaultAsync(u => u.Id == booking.UserId.Value, cancellationToken);
        if (user is null)
        {
            return;
        }

        if (booking.PointsEarned > 0)
        {
            // Có thể refund nhiều đợt (charter cọc + phần còn lại) — chỉ thu phần chưa thu.
            var alreadyRevoked = await context.Set<PointTransaction>()
                .Where(t => t.BookingId == booking.Id
                    && t.TransactionType == PointTransactionTypes.EarnRevoked)
                .SumAsync(t => (int?)-t.Points, cancellationToken) ?? 0;
            // Khách có thể đã tiêu bớt — chỉ thu được tới mức balance còn lại, không để âm.
            var revocablePoints = Math.Min(booking.PointsEarned - alreadyRevoked, user.PointBalance);
            if (revocablePoints > 0)
            {
                AddTransaction(
                    context,
                    user,
                    booking.Id,
                    PointTransactionTypes.EarnRevoked,
                    -revocablePoints,
                    $"Thu hồi điểm đã tích do hoàn tiền booking {booking.BookingCode}",
                    now);
            }
        }

        if (booking.BookingStatus == Domain.Enums.BookingStatus.Refunded)
        {
            await ReturnRedeemedPointsAsync(
                context,
                booking,
                $"Hoàn lại điểm đã dùng do hoàn tiền booking {booking.BookingCode}",
                now,
                cancellationToken);
        }
    }
}
