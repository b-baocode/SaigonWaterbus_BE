using SaigonWaterbus.Application.Auth.Common;

namespace SaigonWaterbus.Application.Seats;

public interface ISeatManagementService
{
    Task<VesselSeatsDto> GetSeatsAsync(Guid vesselId, CancellationToken cancellationToken);

    Task<VesselSeatsDto> GenerateSeatMatrixAsync(GenerateSeatMatrixRequest request, CancellationToken cancellationToken);

    Task<VesselSeatsDto> ConfigureSeatsAsync(GenerateSeatsRequest request, CancellationToken cancellationToken);

    Task<SeatDto> UpdateSeatAsync(UpdateSeatRequest request, CancellationToken cancellationToken);

    Task<SeatDto> UpdateSeatStatusAsync(UpdateSeatStatusRequest request, CancellationToken cancellationToken);

    Task<AuthActionResultDto> DeleteSeatAsync(Guid vesselId, Guid seatId, CancellationToken cancellationToken);

    Task<AuthActionResultDto> DeleteAllSeatsAsync(Guid vesselId, CancellationToken cancellationToken);
}
