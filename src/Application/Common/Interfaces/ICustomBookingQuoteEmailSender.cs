using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Common.Interfaces;

public interface ICustomBookingQuoteEmailSender
{
    Task SendQuoteAsync(CustomBookingRequest request, CancellationToken cancellationToken);
}
