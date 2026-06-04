using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.WaterbusServices;

public sealed record GetWaterbusServiceByIdRequest(int ServiceId);

public sealed class GetWaterbusServiceByIdRequestValidator : AbstractValidator<GetWaterbusServiceByIdRequest>
{
    public GetWaterbusServiceByIdRequestValidator()
    {
        RuleFor(x => x.ServiceId)
            .GreaterThan(0)
            .WithMessage("ServiceId không hợp lệ.");
    }
}

public sealed class GetWaterbusServiceByIdRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetWaterbusServiceByIdRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<WaterbusServiceDto> ExecuteAsync(
        GetWaterbusServiceByIdRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await WaterbusServiceSupport.EnsureCurrentUserCanViewWaterbusServicesAsync(
            _context,
            _userContext,
            cancellationToken);

        var query = _context.WaterbusServices
            .AsNoTracking()
            .AsQueryable();

        var service = await WaterbusServiceSupport.ApplyVisibilityFilter(
                query,
                actor,
                includeInactive: WaterbusServiceSupport.CanManageWaterbusServices(actor))
            .SingleOrDefaultAsync(x => x.Id == request.ServiceId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy dịch vụ WaterBus.");

        return WaterbusServiceSupport.CreateDto(service);
    }
}
