using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Vessels;

public sealed class VesselManagementService : IVesselManagementService
{
    private readonly IRequestValidator _validator;
    private readonly GetVesselsRequestUseCase _getVessels;
    private readonly GetVesselByIdRequestUseCase _getVesselById;
    private readonly CreateVesselRequestUseCase _createVessel;
    private readonly UpdateVesselRequestUseCase _updateVessel;
    private readonly UpdateVesselStatusRequestUseCase _updateVesselStatus;
    private readonly DeleteVesselRequestUseCase _deleteVessel;

    public VesselManagementService(
        IRequestValidator validator,
        GetVesselsRequestUseCase getVessels,
        GetVesselByIdRequestUseCase getVesselById,
        CreateVesselRequestUseCase createVessel,
        UpdateVesselRequestUseCase updateVessel,
        UpdateVesselStatusRequestUseCase updateVesselStatus,
        DeleteVesselRequestUseCase deleteVessel)
    {
        _validator = validator;
        _getVessels = getVessels;
        _getVesselById = getVesselById;
        _createVessel = createVessel;
        _updateVessel = updateVessel;
        _updateVesselStatus = updateVesselStatus;
        _deleteVessel = deleteVessel;
    }

    public async Task<IReadOnlyCollection<VesselDto>> GetVesselsAsync(
        Guid? serviceId,
        VesselStatus? status,
        string? search,
        CancellationToken cancellationToken)
    {
        var request = new GetVesselsRequest(serviceId, status, search);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _getVessels.ExecuteAsync(request, cancellationToken);
    }

    public async Task<VesselDto> GetVesselByIdAsync(Guid vesselId, CancellationToken cancellationToken)
    {
        var request = new GetVesselByIdRequest(vesselId);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _getVesselById.ExecuteAsync(request, cancellationToken);
    }

    public async Task<VesselDto> CreateVesselAsync(CreateVesselRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _createVessel.ExecuteAsync(request, cancellationToken);
    }

    public async Task<VesselDto> UpdateVesselAsync(UpdateVesselRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _updateVessel.ExecuteAsync(request, cancellationToken);
    }

    public async Task<VesselDto> UpdateVesselStatusAsync(UpdateVesselStatusRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _updateVesselStatus.ExecuteAsync(request, cancellationToken);
    }

    public async Task<AuthActionResultDto> DeleteVesselAsync(Guid vesselId, CancellationToken cancellationToken)
    {
        var request = new DeleteVesselRequest(vesselId);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _deleteVessel.ExecuteAsync(request, cancellationToken);
    }
}
