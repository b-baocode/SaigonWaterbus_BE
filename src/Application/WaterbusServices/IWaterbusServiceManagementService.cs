using SaigonWaterbus.Application.Auth.Common;

namespace SaigonWaterbus.Application.WaterbusServices;

public interface IWaterbusServiceManagementService
{
    Task<IReadOnlyCollection<WaterbusServiceDto>> GetServicesAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<WaterbusServiceDto> GetServiceByIdAsync(
        Guid serviceId,
        CancellationToken cancellationToken);

    Task<WaterbusServiceSeatTypesDto> GetServiceSeatTypesAsync(
        Guid serviceId,
        CancellationToken cancellationToken);

    Task<WaterbusServiceDto> CreateServiceAsync(
        CreateWaterbusServiceRequest request,
        CancellationToken cancellationToken);

    Task<WaterbusServiceDto> UpdateServiceAsync(
        UpdateWaterbusServiceRequest request,
        CancellationToken cancellationToken);

    Task<WaterbusServiceDto> UpdateServiceStatusAsync(
        UpdateWaterbusServiceStatusRequest request,
        CancellationToken cancellationToken);

    Task<AuthActionResultDto> DeleteServiceAsync(
        Guid serviceId,
        CancellationToken cancellationToken);
}
