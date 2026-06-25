using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.WaterbusServices;

public sealed record GetWaterbusServiceSeatTypesRequest(Guid ServiceId);

public sealed record WaterbusServiceSeatTypeDto(
    Guid SeatTypeId,
    string Code,
    string Name,
    int DisplayOrder,
    bool IsActive);

public sealed record WaterbusServiceSeatTypesDto(
    Guid ServiceId,
    string ServiceCode,
    BookingMode BookingMode,
    IReadOnlyCollection<WaterbusServiceSeatTypeDto> SeatTypes);

public sealed class GetWaterbusServiceSeatTypesRequestValidator
    : AbstractValidator<GetWaterbusServiceSeatTypesRequest>
{
    public GetWaterbusServiceSeatTypesRequestValidator()
    {
        RuleFor(x => x.ServiceId)
            .NotEmpty()
            .WithMessage("ServiceId không hợp lệ.");
    }
}

public sealed class GetWaterbusServiceSeatTypesRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetWaterbusServiceSeatTypesRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<WaterbusServiceSeatTypesDto> ExecuteAsync(
        GetWaterbusServiceSeatTypesRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await WaterbusServiceSupport.EnsureCurrentUserCanViewWaterbusServicesAsync(
            _context,
            _userContext,
            cancellationToken);

        var service = await WaterbusServiceSupport.ApplyVisibilityFilter(
                _context.WaterbusServices
                    .AsNoTracking()
                    .AsQueryable(),
                actor,
                includeInactive: WaterbusServiceSupport.CanManageWaterbusServices(actor))
            .SingleOrDefaultAsync(x => x.Id == request.ServiceId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException(
                "Không tìm thấy dịch vụ WaterBus.");

        return WaterbusServiceSupport.CreateSeatTypesDto(
            service,
            includeInactive: WaterbusServiceSupport.CanManageWaterbusServices(actor));
    }
}
