using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Boats;

public sealed class BoatManagementService : IBoatManagementService
{
    private readonly IRequestValidator _validator;
    private readonly GetBoatsRequestUseCase _getBoats;
    private readonly GetBoatByIdRequestUseCase _getBoatById;
    private readonly CreateBoatRequestUseCase _createBoat;
    private readonly UpdateBoatRequestUseCase _updateBoat;
    private readonly UpdateBoatStatusRequestUseCase _updateBoatStatus;
    private readonly DeleteBoatRequestUseCase _deleteBoat;

    public BoatManagementService(
        IRequestValidator validator,
        GetBoatsRequestUseCase getBoats,
        GetBoatByIdRequestUseCase getBoatById,
        CreateBoatRequestUseCase createBoat,
        UpdateBoatRequestUseCase updateBoat,
        UpdateBoatStatusRequestUseCase updateBoatStatus,
        DeleteBoatRequestUseCase deleteBoat)
    {
        _validator = validator;
        _getBoats = getBoats;
        _getBoatById = getBoatById;
        _createBoat = createBoat;
        _updateBoat = updateBoat;
        _updateBoatStatus = updateBoatStatus;
        _deleteBoat = deleteBoat;
    }

    public async Task<IReadOnlyCollection<BoatDto>> GetBoatsAsync(
        BoatStatus? status,
        string? search,
        CancellationToken cancellationToken)
    {
        var request = new GetBoatsRequest(status, search);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _getBoats.ExecuteAsync(request, cancellationToken);
    }

    public async Task<BoatDto> GetBoatByIdAsync(Guid boatId, CancellationToken cancellationToken)
    {
        var request = new GetBoatByIdRequest(boatId);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _getBoatById.ExecuteAsync(request, cancellationToken);
    }

    public async Task<BoatDto> CreateBoatAsync(CreateBoatRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _createBoat.ExecuteAsync(request, cancellationToken);
    }

    public async Task<BoatDto> UpdateBoatAsync(UpdateBoatRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _updateBoat.ExecuteAsync(request, cancellationToken);
    }

    public async Task<BoatDto> UpdateBoatStatusAsync(UpdateBoatStatusRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _updateBoatStatus.ExecuteAsync(request, cancellationToken);
    }

    public async Task<AuthActionResultDto> DeleteBoatAsync(Guid boatId, CancellationToken cancellationToken)
    {
        var request = new DeleteBoatRequest(boatId);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _deleteBoat.ExecuteAsync(request, cancellationToken);
    }
}
