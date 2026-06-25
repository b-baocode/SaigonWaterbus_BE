namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IFareCalculator
{
    ///
    Task<decimal> CalculateAsync(
        Guid routeId,
        Guid fromStationId,
        Guid toStationId,
        Guid ticketTypeId,
        CancellationToken cancellationToken);

}
