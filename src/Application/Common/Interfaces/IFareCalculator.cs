namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IFareCalculator
{
    /// <summary>
    /// Tính unit_price = FareMatrix.BasePrice × TicketType.PriceModifier.
    /// Throw NotFoundException nếu không tìm thấy fare hoặc ticket type.
    /// </summary>
    Task<decimal> CalculateAsync(
        Guid routeId,
        Guid fromStationId,
        Guid toStationId,
        Guid ticketTypeId,
        CancellationToken cancellationToken);

}
