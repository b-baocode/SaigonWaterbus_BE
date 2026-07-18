using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Points;

public static class PointTransactionTypes
{
    /// <summary>Tích điểm 1% sau mỗi payment thành công.</summary>
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
    /// <summary>Tỷ lệ tích điểm: 1% giá trị mỗi payment thành công.</summary>
    public const decimal EarnRate = 0.01m;

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
