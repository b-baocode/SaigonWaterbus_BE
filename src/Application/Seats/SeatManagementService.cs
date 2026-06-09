using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Seats;

public sealed class SeatManagementService : ISeatManagementService
{
    private readonly IRequestValidator _validator;
    private readonly GetSeatsRequestUseCase _getSeats;
    private readonly GenerateSeatMatrixRequestUseCase _generateSeatMatrix;
    private readonly GenerateSeatsRequestUseCase _configureSeats;
    private readonly UpdateSeatRequestUseCase _updateSeat;
    private readonly UpdateSeatStatusRequestUseCase _updateSeatStatus;
    private readonly DeleteSeatRequestUseCase _deleteSeat;
    private readonly DeleteAllSeatsRequestUseCase _deleteAllSeats;

    public SeatManagementService(
        IRequestValidator validator,
        GetSeatsRequestUseCase getSeats,
        GenerateSeatMatrixRequestUseCase generateSeatMatrix,
        GenerateSeatsRequestUseCase configureSeats,
        UpdateSeatRequestUseCase updateSeat,
        UpdateSeatStatusRequestUseCase updateSeatStatus,
        DeleteSeatRequestUseCase deleteSeat,
        DeleteAllSeatsRequestUseCase deleteAllSeats)
    {
        _validator = validator;
        _getSeats = getSeats;
        _generateSeatMatrix = generateSeatMatrix;
        _configureSeats = configureSeats;
        _updateSeat = updateSeat;
        _updateSeatStatus = updateSeatStatus;
        _deleteSeat = deleteSeat;
        _deleteAllSeats = deleteAllSeats;
    }

    public async Task<VesselSeatsDto> GetSeatsAsync(Guid vesselId, CancellationToken cancellationToken)
    {
        var request = new GetSeatsRequest(vesselId);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _getSeats.ExecuteAsync(request, cancellationToken);
    }

    public async Task<VesselSeatsDto> GenerateSeatMatrixAsync(GenerateSeatMatrixRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _generateSeatMatrix.ExecuteAsync(request, cancellationToken);
    }

    public async Task<VesselSeatsDto> ConfigureSeatsAsync(GenerateSeatsRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _configureSeats.ExecuteAsync(request, cancellationToken);
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

    public async Task<AuthActionResultDto> DeleteSeatAsync(Guid vesselId, Guid seatId, CancellationToken cancellationToken)
    {
        var request = new DeleteSeatRequest(vesselId, seatId);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _deleteSeat.ExecuteAsync(request, cancellationToken);
    }

    public async Task<AuthActionResultDto> DeleteAllSeatsAsync(Guid vesselId, CancellationToken cancellationToken)
    {
        var request = new DeleteAllSeatsRequest(vesselId);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _deleteAllSeats.ExecuteAsync(request, cancellationToken);
    }
}
