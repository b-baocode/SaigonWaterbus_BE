namespace SaigonWaterbus.Application.Seats;

public static class SeatTypePricing
{
    public static decimal GetBasePrice(string seatTypeCode) =>
        Normalize(seatTypeCode) switch
        {
            "STANDARD" => 7_000m,
            "CABIN" => 10_000m,
            "RIVER" => 12_000m,
            "SKY" => 15_000m,
            _ => throw new ArgumentOutOfRangeException(nameof(seatTypeCode), "Unknown seat type.")
        };

    public static bool TryGetBasePrice(string seatTypeCode, out decimal basePrice)
    {
        try
        {
            basePrice = GetBasePrice(seatTypeCode);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            basePrice = 0;
            return false;
        }
    }

    private static string Normalize(string value) =>
        value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}
