using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;
using ValidationFailure = FluentValidation.Results.ValidationFailure;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record GetCharterBookingRefundPreviewQuery(Guid Id)
    : IRequest<CharterBookingRefundPreviewDto>;

public sealed class GetCharterBookingRefundPreviewQueryValidator
    : AbstractValidator<GetCharterBookingRefundPreviewQuery>
{
    public GetCharterBookingRefundPreviewQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public sealed class GetCharterBookingRefundPreviewQueryHandler
    : IRequestHandler<GetCharterBookingRefundPreviewQuery, CharterBookingRefundPreviewDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public GetCharterBookingRefundPreviewQueryHandler(
        IApplicationDbContext db,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _db = db;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<CharterBookingRefundPreviewDto> Handle(
        GetCharterBookingRefundPreviewQuery request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_db)
            .SingleOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        if (booking.UserId != userId)
        {
            throw new NotFoundException("Charter booking not found.");
        }

        var now = _timeProvider.GetUtcNow();
        var departure = CombineDeparture(booking.DepartureDate, booking.StartTime);
        var hoursToDeparture = departure.HasValue && departure.Value > now
            ? (decimal)(departure.Value - now).TotalHours
            : -1m;
        var policyPercent = ComputePolicyPercent(hoursToDeparture);

        var payments = await _db.Set<Payment>()
            .Where(p => p.BookingId == request.Id)
            .ToListAsync(cancellationToken);

        var totalPaid = payments
            .Where(p => p.PaymentStatus == BookingPaymentStatusExtensions.PaidValue)
            .Sum(p => p.Amount);
        // Tổng đã hoàn lấy từ payment.RefundAmount thực tế (không lọc theo PaymentStatus),
        // vì partial refund giữ payment.PaymentStatus = "Paid" nhưng đã có RefundAmount > 0.
        var totalRefunded = payments.Sum(p => p.RefundAmount);

        var outstanding = Math.Max(0m, totalPaid - totalRefunded);

        // Chính sách không hoàn tiền (huỷ dưới 24 giờ trước giờ kh�i hành) vẫn cho phép "đóng sổ" booking
        // thông qua POST /payments/{id}/refund với refundAmount = 0 → BE đánh dấu Refunded.
        // Booking đã Cancelled/Completed/Expired không còn cho refund (cancel & refund là 2 flow tách biệt).
        var policyCap = Math.Floor(totalPaid * policyPercent);
        var outstandingRefundAmount = outstanding;

        var canRequestRefund = outstanding > 0m
            && (policyPercent > 0m || booking.BookingStatus == BookingStatus.Cancelled)
            && booking.BookingStatus != BookingStatus.Completed
            && booking.BookingStatus != BookingStatus.Expired;

        var isPartiallyRefunded = outstanding > 0m && totalRefunded > 0m && totalPaid > 0m;
        var isFullyRefunded = outstanding == 0m && totalPaid > 0m;

        var items = new List<RefundablePaymentDto>();
        var remaining = outstandingRefundAmount;
        // Cap tổng theo policy (giống CharterBookingRefundSupport.GetRefundablePayments):
        // - policyPercent = 0% → cap = 0 (chỉ đóng sổ booking, không refund tiền)
        // - policyPercent > 0% → cap = floor(totalPaid * policyPercent)
        var distributed = 0m;
        foreach (var p in payments
            .Where(p => p.PaymentStatus == BookingPaymentStatusExtensions.PaidValue)
            .OrderBy(p => p.Created))
        {
            var paymentOutstanding = Math.Max(0m, p.Amount - p.RefundAmount);
            // Số tiền có thể hoàn cho payment này = min(còn lại của payment, còn lại của tổng outstanding, cap còn lại).
            var available = Math.Min(paymentOutstanding, Math.Max(0m, remaining));
            available = Math.Min(available, policyCap - distributed);
            if (available < 0m) available = 0m;
            distributed += available;
            if (available > 0m)
            {
                items.Add(new RefundablePaymentDto(p.Id, p.Amount, p.RefundAmount, available));
            }
            remaining = Math.Max(0m, remaining - available);
        }

        return new CharterBookingRefundPreviewDto(
            booking.Id,
            totalPaid,
            totalRefunded,
            outstandingRefundAmount,
            policyPercent,
            canRequestRefund,
            items,
            isPartiallyRefunded,
            isFullyRefunded);
    }

    private static DateTimeOffset? CombineDeparture(DateOnly? date, TimeOnly? time)
    {
        if (date is null || time is null) return null;
        var d = date.Value;
        var t = time.Value;
        // Giờ khởi hành lưu theo giờ Việt Nam (UTC+7), KHÔNG dùng UTC.
        return new DateTimeOffset(
            d.Year, d.Month, d.Day,
            t.Hour, t.Minute, t.Second,
            TimeSpan.FromHours(7));
    }

    private static decimal ComputePolicyPercent(decimal hoursToDeparture)
    {
        if (hoursToDeparture < 0m) return 0m;
        if (hoursToDeparture >= 72m) return 1.0m;
        if (hoursToDeparture >= 24m) return 0.7m;
        return 0m;
    }
}

public sealed record CharterBookingRefundPreviewDto(
    Guid BookingId,
    decimal TotalPaidAmount,
    decimal TotalRefundedAmount,
    decimal OutstandingRefundAmount,
    decimal PolicyPercent,
    bool CanRequestRefund,
    IReadOnlyList<RefundablePaymentDto> RefundablePayments,
    /// <summary>True khi booking đã được hoàn một phần (TotalRefundedAmount > 0 và OutstandingRefundAmount > 0).</summary>
    bool IsPartiallyRefunded = false,
    /// <summary>True khi booking đã hoàn đủ toàn bộ (OutstandingRefundAmount = 0 và TotalPaidAmount > 0).</summary>
    bool IsFullyRefunded = false);

public sealed record RefundablePaymentDto(
    Guid PaymentId,
    decimal PaidAmount,
    decimal AlreadyRefundedAmount,
    decimal AvailableRefundAmount);
