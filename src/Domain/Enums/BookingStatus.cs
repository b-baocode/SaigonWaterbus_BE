namespace SaigonWaterbus.Domain.Enums;

public enum BookingStatus
{
    PendingPayment = 0,
    Confirmed = 1,
    Cancelled = 2,
    Expired = 3,
    Quoted = 4,
    Completed = 5,
    PendingQuote = 6,
    Overdue = 7
}
