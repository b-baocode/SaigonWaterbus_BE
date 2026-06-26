using SaigonWaterbus.Application.Auth.Common;

namespace SaigonWaterbus.Application.Seats;

public interface ISeatManagementService
{
    Task<BoatSeatsDto> GetSeatsAsync(Guid boatId, CancellationToken cancellationToken);

    Task<BoatSeatsDto> GenerateSeatMatrixAsync(GenerateSeatMatrixRequest request, CancellationToken cancellationToken);

    Task<BoatSeatsDto> ConfigureSeatsAsync(GenerateSeatsRequest request, CancellationToken cancellationToken);

    Task<SeatDto> UpdateSeatAsync(UpdateSeatRequest request, CancellationToken cancellationToken);

    Task<SeatDto> UpdateSeatStatusAsync(UpdateSeatStatusRequest request, CancellationToken cancellationToken);

    Task<AuthActionResultDto> DeleteSeatAsync(Guid boatId, Guid seatId, CancellationToken cancellationToken);

    Task<AuthActionResultDto> DeleteAllSeatsAsync(Guid boatId, CancellationToken cancellationToken);
}
