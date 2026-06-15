using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.WaterbusServices;

public sealed class WaterbusServiceManagementService : IWaterbusServiceManagementService
{
    private readonly IRequestValidator _validator;
    private readonly GetWaterbusServicesRequestUseCase _getServices;
    private readonly GetWaterbusServiceByIdRequestUseCase _getServiceById;
    private readonly GetWaterbusServiceSeatTypesRequestUseCase _getServiceSeatTypes;
    private readonly UpdateWaterbusServiceSeatPriceRequestUseCase _updateServiceSeatPrice;
    private readonly CreateWaterbusServiceRequestUseCase _createService;
    private readonly UpdateWaterbusServiceRequestUseCase _updateService;
    private readonly UpdateWaterbusServiceStatusRequestUseCase _updateServiceStatus;
    private readonly DeleteWaterbusServiceRequestUseCase _deleteService;

    public WaterbusServiceManagementService(
        IRequestValidator validator,
        GetWaterbusServicesRequestUseCase getServices,
        GetWaterbusServiceByIdRequestUseCase getServiceById,
        GetWaterbusServiceSeatTypesRequestUseCase getServiceSeatTypes,
        UpdateWaterbusServiceSeatPriceRequestUseCase updateServiceSeatPrice,
        CreateWaterbusServiceRequestUseCase createService,
        UpdateWaterbusServiceRequestUseCase updateService,
        UpdateWaterbusServiceStatusRequestUseCase updateServiceStatus,
        DeleteWaterbusServiceRequestUseCase deleteService)
    {
        _validator = validator;
        _getServices = getServices;
        _getServiceById = getServiceById;
        _getServiceSeatTypes = getServiceSeatTypes;
        _updateServiceSeatPrice = updateServiceSeatPrice;
        _createService = createService;
        _updateService = updateService;
        _updateServiceStatus = updateServiceStatus;
        _deleteService = deleteService;
    }

    public async Task<IReadOnlyCollection<WaterbusServiceDto>> GetServicesAsync(
        bool includeInactive,
        CancellationToken cancellationToken) =>
        await _getServices.ExecuteAsync(new GetWaterbusServicesRequest(includeInactive), cancellationToken);

    public async Task<WaterbusServiceDto> GetServiceByIdAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        var request = new GetWaterbusServiceByIdRequest(serviceId);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _getServiceById.ExecuteAsync(request, cancellationToken);
    }

    public async Task<WaterbusServiceSeatTypesDto> GetServiceSeatTypesAsync(
        Guid serviceId,
        CancellationToken cancellationToken)
    {
        var request = new GetWaterbusServiceSeatTypesRequest(serviceId);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _getServiceSeatTypes.ExecuteAsync(request, cancellationToken);
    }

    public async Task<WaterbusServiceSeatTypesDto> UpdateServiceSeatPriceAsync(
        UpdateWaterbusServiceSeatPriceRequest request,
        CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _updateServiceSeatPrice.ExecuteAsync(request, cancellationToken);
    }

    public async Task<WaterbusServiceDto> CreateServiceAsync(
        CreateWaterbusServiceRequest request,
        CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _createService.ExecuteAsync(request, cancellationToken);
    }

    public async Task<WaterbusServiceDto> UpdateServiceAsync(
        UpdateWaterbusServiceRequest request,
        CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _updateService.ExecuteAsync(request, cancellationToken);
    }

    public async Task<WaterbusServiceDto> UpdateServiceStatusAsync(
        UpdateWaterbusServiceStatusRequest request,
        CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _updateServiceStatus.ExecuteAsync(request, cancellationToken);
    }

    public async Task<AuthActionResultDto> DeleteServiceAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        var request = new DeleteWaterbusServiceRequest(serviceId);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _deleteService.ExecuteAsync(request, cancellationToken);
    }
}
