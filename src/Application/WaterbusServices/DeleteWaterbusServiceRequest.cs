using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.WaterbusServices;

public sealed record DeleteWaterbusServiceRequest(int ServiceId);

public sealed class DeleteWaterbusServiceRequestValidator : AbstractValidator<DeleteWaterbusServiceRequest>
{
    public DeleteWaterbusServiceRequestValidator()
    {
        RuleFor(x => x.ServiceId)
            .GreaterThan(0)
            .WithMessage("ServiceId không hợp lệ.");
    }
}

public sealed class DeleteWaterbusServiceRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public DeleteWaterbusServiceRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<AuthActionResultDto> ExecuteAsync(
        DeleteWaterbusServiceRequest request,
        CancellationToken cancellationToken)
    {
        await WaterbusServiceSupport.EnsureCurrentUserCanManageWaterbusServicesAsync(
            _context,
            _userContext,
            cancellationToken);

        var service = await _context.WaterbusServices
            .SingleOrDefaultAsync(x => x.Id == request.ServiceId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy dịch vụ WaterBus.");

        _context.WaterbusServices.Remove(service);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthActionResultDto("Xoa dich vu WaterBus thanh cong.");
    }
}
