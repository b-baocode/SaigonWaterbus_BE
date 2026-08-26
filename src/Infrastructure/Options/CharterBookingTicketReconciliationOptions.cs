namespace SaigonWaterbus.Infrastructure.Options;

public sealed class CharterBookingTicketReconciliationOptions
{
    public const string SectionName = "CharterBookingTicketReconciliation";

    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 60;
}
