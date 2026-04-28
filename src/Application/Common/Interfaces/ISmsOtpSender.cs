using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Common.Interfaces;

public interface ISmsOtpSender
{
    Task SendAsync(string phoneNumber, string code, OtpPurpose purpose, string? recipientName, CancellationToken cancellationToken);
}
