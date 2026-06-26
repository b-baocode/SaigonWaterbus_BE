using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Boats;

public interface IBoatManagementService
{
    Task<IReadOnlyCollection<BoatDto>> GetBoatsAsync(
        BoatStatus? status,
        string? search,
        CancellationToken cancellationToken);

    Task<BoatDto> GetBoatByIdAsync(Guid boatId, CancellationToken cancellationToken);

    Task<BoatDto> CreateBoatAsync(CreateBoatRequest request, CancellationToken cancellationToken);

    Task<BoatDto> UpdateBoatAsync(UpdateBoatRequest request, CancellationToken cancellationToken);

    Task<BoatDto> UpdateBoatStatusAsync(UpdateBoatStatusRequest request, CancellationToken cancellationToken);

    Task<AuthActionResultDto> DeleteBoatAsync(Guid boatId, CancellationToken cancellationToken);
}
