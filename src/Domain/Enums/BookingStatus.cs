namespace SaigonWaterbus.Domain.Enums;

public enum BookingStatus
{
    PendingPayment = 0,
    Confirmed = 1,
    Cancelled = 2,
    Expired = 3,
    Refunded = 4,
    Quoted = 5,
    Completed = 6,
    PendingQuote = 7
}
