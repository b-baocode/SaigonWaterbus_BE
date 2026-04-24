using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class IdentityNormalizer : IIdentityNormalizer
{
    public string NormalizePhone(string phoneNumber)
    {
        Guard.Against.NullOrWhiteSpace(phoneNumber);

        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits))
        {
            throw new ArgumentException("Phone number is invalid.", nameof(phoneNumber));
        }

        if (digits.StartsWith("84", StringComparison.Ordinal) && digits.Length >= 11)
        {
            digits = $"0{digits[2..]}";
        }
        else if (!digits.StartsWith('0') && digits.Length == 9)
        {
            digits = $"0{digits}";
        }

        return digits;
    }

    public string NormalizeEmail(string email)
    {
        Guard.Against.NullOrWhiteSpace(email);

        return email.Trim().ToUpperInvariant();
    }
}
