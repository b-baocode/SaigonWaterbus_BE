using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.CustomBookingRequests;

internal sealed record CustomBookingRefundQuote(decimal Percent, decimal Amount, string Note);

internal static class CustomBookingRefundPolicy
{
    private static readonly TimeSpan VietnamUtcOffset = TimeSpan.FromHours(7);
    private static readonly TimeSpan FullRefundThreshold = TimeSpan.FromDays(3);
    private static readonly TimeSpan PartialRefundThreshold = TimeSpan.FromHours(24);

    public static CustomBookingRefundQuote Calculate(
        CustomBookingRequest request,
        decimal paidAmount,
        DateTimeOffset utcNow)
    {
        if (paidAmount <= 0)
        {
            return new CustomBookingRefundQuote(0m, 0m, "Booking chưa có khoản thanh toán để hoàn.");
        }

        var departureAt = GetDepartureAtOrThrow(request);
        var remaining = departureAt - utcNow.ToUniversalTime();
        var percent = remaining >= FullRefundThreshold
            ? 100m
            : remaining >= PartialRefundThreshold
                ? 30m
                : 0m;

        var note = percent switch
        {
            100m => "Hủy trước giờ khởi hành ít nhất 3 ngày: hoàn 100% số tiền đã thanh toán.",
            30m => "Hủy trước giờ khởi hành ít nhất 24 giờ: hoàn 30% số tiền đã thanh toán.",
            _ => "Hủy trong vòng 24 giờ trước giờ khởi hành: không hoàn tiền."
        };

        return new CustomBookingRefundQuote(
            percent,
            decimal.Round(paidAmount * percent / 100m, 0, MidpointRounding.AwayFromZero),
            note);
    }

    public static DateTimeOffset GetDepartureAtOrThrow(CustomBookingRequest request)
    {
        if (!request.PreferredStartTime.HasValue)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.PreferredStartTime),
                "Booking chưa có giờ khởi hành nên chưa thể tính chính sách hoàn tiền.");
        }

        return new DateTimeOffset(
                request.DepartureDate.ToDateTime(request.PreferredStartTime.Value),
                VietnamUtcOffset)
            .ToUniversalTime();
    }
}
