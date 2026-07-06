namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IFareCalculator
{
    Task<decimal> CalculateAsync(
        Guid seatId,
        string ticketTypeCode,
        CancellationToken cancellationToken,
        Guid? tripId = null);

}
