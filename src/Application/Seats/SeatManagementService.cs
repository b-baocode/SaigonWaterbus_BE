using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Seats;

public sealed class SeatManagementService : ISeatManagementService
{
    private readonly IRequestValidator _validator;
    private readonly GetSeatsRequestUseCase _getSeats;
    private readonly GenerateSeatsRequestUseCase _generateSeats;
    private readonly UpdateSeatRequestUseCase _updateSeat;
    private readonly UpdateSeatStatusRequestUseCase _updateSeatStatus;
    private readonly DeleteSeatRequestUseCase _deleteSeat;
    private readonly DeleteAllSeatsRequestUseCase _deleteAllSeats;

    public SeatManagementService(
        IRequestValidator validator,
        GetSeatsRequestUseCase getSeats,
        GenerateSeatsRequestUseCase generateSeats,
        UpdateSeatRequestUseCase updateSeat,
        UpdateSeatStatusRequestUseCase updateSeatStatus,
        DeleteSeatRequestUseCase deleteSeat,
        DeleteAllSeatsRequestUseCase deleteAllSeats)
    {
        _validator = validator;
        _getSeats = getSeats;
        _generateSeats = generateSeats;
        _updateSeat = updateSeat;
        _updateSeatStatus = updateSeatStatus;
        _deleteSeat = deleteSeat;
        _deleteAllSeats = deleteAllSeats;
    }

    public async Task<VesselSeatsDto> GetSeatsAsync(int vesselId, CancellationToken cancellationToken)
    {
        var request = new GetSeatsRequest(vesselId);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _getSeats.ExecuteAsync(request, cancellationToken);
    }

    public async Task<VesselSeatsDto> GenerateSeatsAsync(GenerateSeatsRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _generateSeats.ExecuteAsync(request, cancellationToken);
    }

    public async Task<SeatDto> UpdateSeatAsync(UpdateSeatRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _updateSeat.ExecuteAsync(request, cancellationToken);
    }

    public async Task<SeatDto> UpdateSeatStatusAsync(UpdateSeatStatusRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _updateSeatStatus.ExecuteAsync(request, cancellationToken);
    }

    public async Task<AuthActionResultDto> DeleteSeatAsync(int vesselId, int seatId, CancellationToken cancellationToken)
    {
        var request = new DeleteSeatRequest(vesselId, seatId);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _deleteSeat.ExecuteAsync(request, cancellationToken);
    }

    public async Task<AuthActionResultDto> DeleteAllSeatsAsync(int vesselId, CancellationToken cancellationToken)
    {
        var request = new DeleteAllSeatsRequest(vesselId);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _deleteAllSeats.ExecuteAsync(request, cancellationToken);
    }
}
