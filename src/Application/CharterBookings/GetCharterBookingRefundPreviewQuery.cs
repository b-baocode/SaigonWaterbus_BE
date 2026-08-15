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
        var totalRefunded = payments
            .Where(p => p.PaymentStatus == BookingPaymentStatusExtensions.RefundedValue)
            .Sum(p => p.RefundAmount);

        var outstanding = Math.Max(0m, totalPaid - totalRefunded);

        var canRequestRefund = outstanding > 0m
            && policyPercent > 0m
            && booking.BookingStatus != BookingStatus.Completed
            && booking.BookingStatus != BookingStatus.Expired;

        var items = new List<RefundablePaymentDto>();
        var remaining = outstanding;
        foreach (var p in payments
            .Where(p => p.PaymentStatus == BookingPaymentStatusExtensions.PaidValue)
            .OrderBy(p => p.Created))
        {
            var available = Math.Min(p.Amount, Math.Max(0m, remaining));
            items.Add(new RefundablePaymentDto(p.Id, p.Amount, available));
            remaining = Math.Max(0m, remaining - available);
            if (remaining <= 0m) break;
        }

        return new CharterBookingRefundPreviewDto(
            booking.Id,
            totalPaid,
            totalRefunded,
            outstanding,
            policyPercent,
            canRequestRefund,
            items);
    }

    private static DateTimeOffset? CombineDeparture(DateOnly? date, TimeOnly? time)
    {
        if (date is null || time is null) return null;
        var d = date.Value;
        var t = time.Value;
        return new DateTimeOffset(
            d.Year, d.Month, d.Day,
            t.Hour, t.Minute, t.Second,
            TimeSpan.Zero);
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
    IReadOnlyList<RefundablePaymentDto> RefundablePayments);

public sealed record RefundablePaymentDto(
    Guid PaymentId,
    decimal PaidAmount,
    decimal AvailableRefundAmount);
