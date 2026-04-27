namespace SaigonWaterbus.Domain.Constants;

public static class PhoneRules
{
    public const int RequiredDigits = 10;

    public static bool IsValid(string? phoneNumber) =>
        TryNormalize(phoneNumber, out _);

    public static bool TryNormalize(string? phoneNumber, out string normalizedPhoneNumber)
    {
        normalizedPhoneNumber = string.Empty;

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return false;
        }

        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits))
        {
            return false;
        }

        if (digits.StartsWith("84", StringComparison.Ordinal) && digits.Length == 11)
        {
            digits = $"0{digits[2..]}";
        }
        else if (!digits.StartsWith('0') && digits.Length == 9)
        {
            digits = $"0{digits}";
        }

        if (digits.Length != RequiredDigits || !digits.StartsWith('0'))
        {
            return false;
        }

        normalizedPhoneNumber = digits;
        return true;
    }
}
