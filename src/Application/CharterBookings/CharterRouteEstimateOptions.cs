namespace SaigonWaterbus.Application.CharterBookings;

public sealed class CharterRouteEstimateOptions
{
    public const string SectionName = "CharterRouteEstimate";

    public decimal AverageSpeedKmh { get; set; } = 13m;

    public decimal BufferPercent { get; set; } = 0.10m;

    public int MinimumBufferMinutes { get; set; }

    public int FixedBufferMinutes { get; set; }

    public int FreeStayMinutesPerBooking { get; set; } = 30;
}

public static class CharterRouteEstimateOptionsSetup
{
    public static void ConfigureDefaults(CharterRouteEstimateOptions? options) =>
        CharterBookingRoutePricingSupport.ConfigureDefaults(options);
}
