namespace SaigonWaterbus.Application.Common;

public static class PriceRoundingSupport
{
    public const decimal VndRoundingStep = 1000m;

    /// <summary>Làm tròn tiền VND về nghìn gần nhất: 12.160 -> 12.000, 12.560 -> 13.000.</summary>
    public static decimal RoundVndToNearestThousand(decimal amount)
    {
        if (amount == 0)
        {
            return 0;
        }

        var sign = amount < 0 ? -1m : 1m;
        var absoluteAmount = Math.Abs(amount);
        return sign * Math.Floor((absoluteAmount + VndRoundingStep / 2m) / VndRoundingStep) * VndRoundingStep;
    }

    public static decimal RoundFare(decimal amount) =>
        amount <= 0 ? 0 : RoundVndToNearestThousand(amount);
}
