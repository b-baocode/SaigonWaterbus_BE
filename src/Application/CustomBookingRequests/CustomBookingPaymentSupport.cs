using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CustomBookingRequests;

internal static class CustomBookingPaymentSupport
{
    private static readonly TimeSpan RemainingPaymentDeadlineBeforeDeparture = TimeSpan.FromHours(24);

    public static bool IsFullyPaid(CustomBookingQuote? quote)
    {
        if (quote is null || quote.DepositPaymentStatus != CustomBookingDepositPaymentStatus.Paid)
        {
            return false;
        }

        return quote.RemainingAmount <= 0
            || quote.RemainingPaymentStatus == CustomBookingDepositPaymentStatus.Paid;
    }

    public static DateTimeOffset ResolveRemainingPaymentDeadline(CustomBookingRequest request) =>
        CustomBookingRefundPolicy.GetDepartureAtOrThrow(request)
            .Add(-RemainingPaymentDeadlineBeforeDeparture);

    public static async Task<long> GeneratePaymentOrderCodeAsync(
        IApplicationDbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var baseCode = now.ToUnixTimeMilliseconds() % 1_000_000_000_000L;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var orderCode = (baseCode * 100) + attempt;
            if (!await context.Set<CustomBookingQuote>()
                    .AnyAsync(x =>
                        x.DepositPaymentOrderCode == orderCode
                        || x.RemainingPaymentOrderCode == orderCode,
                        cancellationToken))
            {
                return orderCode;
            }
        }

        throw AuthSupport.CreateValidationException("payment", "Không thể tạo mã thanh toán duy nhất. Vui lòng thử lại.");
    }

    public static long ToPayOsAmount(decimal amount, string propertyName, string errorMessage)
    {
        if (amount <= 0 || decimal.Truncate(amount) != amount || amount > long.MaxValue)
        {
            throw AuthSupport.CreateValidationException(propertyName, errorMessage);
        }

        return (long)amount;
    }

    public static string CreatePaymentDescription(CustomBookingRequest request) =>
        $"{request.DepartureDate:yyMMdd}{request.Id.ToString("N")[^3..].ToUpperInvariant()}";

    public static string CreateBookingReference(CustomBookingRequest request) =>
        $"CB-{request.DepartureDate:yyyyMMdd}-{request.Id.ToString("N")[^6..].ToUpperInvariant()}";
}
