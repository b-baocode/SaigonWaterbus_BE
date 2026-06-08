using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.WaterbusServices;

public sealed record UpdateWaterbusServiceStatusRequest(
    Guid ServiceId,
    bool IsActive);

public sealed class UpdateWaterbusServiceStatusRequestValidator : AbstractValidator<UpdateWaterbusServiceStatusRequest>
{
    public UpdateWaterbusServiceStatusRequestValidator()
    {
        RuleFor(x => x.ServiceId)
            .NotEmpty()
            .WithMessage("ServiceId không hợp lệ.");
    }
}

public sealed class UpdateWaterbusServiceStatusRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public UpdateWaterbusServiceStatusRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<WaterbusServiceDto> ExecuteAsync(
        UpdateWaterbusServiceStatusRequest request,
        CancellationToken cancellationToken)
    {
        await WaterbusServiceSupport.EnsureCurrentUserCanManageWaterbusServicesAsync(
            _context,
            _userContext,
            cancellationToken);

        var service = await _context.WaterbusServices
            .SingleOrDefaultAsync(x => x.Id == request.ServiceId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy dịch vụ WaterBus.");

        service.IsActive = request.IsActive;
        await _context.SaveChangesAsync(cancellationToken);

        return WaterbusServiceSupport.CreateDto(service);
    }
}
