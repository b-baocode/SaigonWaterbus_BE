namespace SaigonWaterbus.Domain.Enums;

public enum BookingPaymentStatus
{
    Unpaid = 0,
    Paid = 1,
    Refunded = 2,
    DepositPaid = 4,
    Failed = 5
}
