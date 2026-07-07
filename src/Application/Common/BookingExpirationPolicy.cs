namespace SaigonWaterbus.Application.Common;

public static class BookingExpirationPolicy
{
    public static TimeSpan PaymentLinkTtl => TimeSpan.FromMinutes(5);

    public static TimeSpan CharterQuoteResponseTtl => TimeSpan.FromHours(2);

    public static TimeSpan CharterPaymentCompletionTtl => TimeSpan.FromHours(12);
}
