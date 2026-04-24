namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IIdentityNormalizer
{
    string NormalizePhone(string phoneNumber);

    string NormalizeEmail(string email);
}
