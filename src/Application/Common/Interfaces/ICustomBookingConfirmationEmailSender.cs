using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Common.Interfaces;

public interface ICustomBookingConfirmationEmailSender
{
    Task SendConfirmationAsync(CustomBookingRequest request, CancellationToken cancellationToken);
}
