using SaigonWaterbus.Application.Auth.Common;

namespace SaigonWaterbus.Application.Seats;

public interface ISeatManagementService
{
    Task<VesselSeatsDto> GetSeatsAsync(int vesselId, CancellationToken cancellationToken);

    Task<VesselSeatsDto> GenerateSeatsAsync(GenerateSeatsRequest request, CancellationToken cancellationToken);

    Task<SeatDto> UpdateSeatAsync(UpdateSeatRequest request, CancellationToken cancellationToken);

    Task<SeatDto> UpdateSeatStatusAsync(UpdateSeatStatusRequest request, CancellationToken cancellationToken);

    Task<AuthActionResultDto> DeleteSeatAsync(int vesselId, int seatId, CancellationToken cancellationToken);

    Task<AuthActionResultDto> DeleteAllSeatsAsync(int vesselId, CancellationToken cancellationToken);
}
