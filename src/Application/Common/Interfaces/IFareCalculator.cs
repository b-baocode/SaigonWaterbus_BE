namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IFareCalculator
{
    ///
    Task<decimal> CalculateAsync(
        Guid seatId,
        Guid ticketTypeId,
        CancellationToken cancellationToken);

}
