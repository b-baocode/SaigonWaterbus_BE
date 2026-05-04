using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class IdentityNormalizer : IIdentityNormalizer
{
    public string NormalizePhone(string phoneNumber)
    {
        Guard.Against.NullOrWhiteSpace(phoneNumber);

        if (!PhoneRules.TryNormalize(phoneNumber, out var normalizedPhoneNumber))
        {
            throw new ArgumentException(PhoneRules.InvalidInternationalPhoneMessage, nameof(phoneNumber));
        }

        return normalizedPhoneNumber;
    }

    public string NormalizeEmail(string email)
    {
        Guard.Against.NullOrWhiteSpace(email);

        return email.Trim().ToUpperInvariant();
    }
}
