using PhoneNumbers;

namespace SaigonWaterbus.Domain.Constants;

public static class PhoneRules
{
    private const string DefaultRegion = "VN";
    private const int VietnamCountryCode = 84;

    public const string InvalidInternationalPhoneMessage =
        "Số điện thoại không hợp lệ. Hệ thống chỉ hỗ trợ số điện thoại Việt Nam, ví dụ 0901234567 hoặc +84901234567.";

    public static bool IsValid(string? phoneNumber) =>
        TryNormalize(phoneNumber, out _);

    public static bool IsVietnamPhone(string? phoneNumber) =>
        TryParse(phoneNumber, out var parsedPhoneNumber) && parsedPhoneNumber.CountryCode == VietnamCountryCode;

    public static bool TryNormalize(string? phoneNumber, out string normalizedPhoneNumber)
    {
        normalizedPhoneNumber = string.Empty;

        if (!TryParse(phoneNumber, out var parsedPhoneNumber))
        {
            return false;
        }

        var phoneNumberUtil = PhoneNumberUtil.GetInstance();
        normalizedPhoneNumber = phoneNumberUtil.Format(parsedPhoneNumber, PhoneNumberFormat.E164);
        return true;
    }

    public static string ToInternationalFormat(string phoneNumber)
    {
        if (!TryNormalize(phoneNumber, out var normalizedPhoneNumber))
        {
            throw new ArgumentException(InvalidInternationalPhoneMessage, nameof(phoneNumber));
        }

        return normalizedPhoneNumber;
    }

    private static bool TryParse(string? phoneNumber, out PhoneNumber parsedPhoneNumber)
    {
        parsedPhoneNumber = new PhoneNumber();

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return false;
        }

        try
        {
            var phoneNumberUtil = PhoneNumberUtil.GetInstance();
            var trimmedPhoneNumber = phoneNumber.Trim();
            if (trimmedPhoneNumber.Any(x => !char.IsDigit(x) && x is not '+' and not ' ' and not '-' and not '.' and not '(' and not ')'))
            {
                return false;
            }

            if (trimmedPhoneNumber.Count(x => x == '+') > 1
                || (trimmedPhoneNumber.Contains('+', StringComparison.Ordinal) && !trimmedPhoneNumber.StartsWith('+')))
            {
                return false;
            }

            parsedPhoneNumber = phoneNumberUtil.Parse(
                trimmedPhoneNumber,
                trimmedPhoneNumber.StartsWith('+') ? null : DefaultRegion);

            if (parsedPhoneNumber.CountryCode != VietnamCountryCode
                || !phoneNumberUtil.IsValidNumberForRegion(parsedPhoneNumber, DefaultRegion))
            {
                return false;
            }

            return true;
        }
        catch (NumberParseException)
        {
            return false;
        }
    }
}
