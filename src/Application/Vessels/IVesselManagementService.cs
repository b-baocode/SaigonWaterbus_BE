using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Vessels;

public interface IVesselManagementService
{
    Task<IReadOnlyCollection<VesselDto>> GetVesselsAsync(
        int? serviceId,
        VesselStatus? status,
        string? search,
        CancellationToken cancellationToken);

    Task<VesselDto> GetVesselByIdAsync(int vesselId, CancellationToken cancellationToken);

    Task<VesselDto> CreateVesselAsync(CreateVesselRequest request, CancellationToken cancellationToken);

    Task<VesselDto> UpdateVesselAsync(UpdateVesselRequest request, CancellationToken cancellationToken);

    Task<VesselDto> UpdateVesselStatusAsync(UpdateVesselStatusRequest request, CancellationToken cancellationToken);

    Task<AuthActionResultDto> DeleteVesselAsync(int vesselId, CancellationToken cancellationToken);
}
