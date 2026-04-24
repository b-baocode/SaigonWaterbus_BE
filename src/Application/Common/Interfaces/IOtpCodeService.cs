namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IOtpCodeService
{
    string GenerateCode();

    string MaskEmail(string email);
}
