namespace SaigonWaterbus.Application.Common.Interfaces;

public interface ISeatAvailabilityService
{
    /// <summary>
    /// Trả về list seat_id trong requestedSeatIds còn available cho trip.
    /// Available = không có SeatHold Active chưa hết hạn VÀ không có BookingItem Active/Boarded.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAvailableSeatsAsync(
        Guid tripId,
        IEnumerable<Guid> requestedSeatIds,
        CancellationToken cancellationToken);
}
