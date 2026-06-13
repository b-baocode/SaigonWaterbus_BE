using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Vessels;

public sealed record AssignVesselServiceRequest(
    Guid VesselId,
    Guid WaterbusServiceId);

public sealed class AssignVesselServiceRequestValidator : AbstractValidator<AssignVesselServiceRequest>
{
    public AssignVesselServiceRequestValidator()
    {
        RuleFor(x => x.VesselId)
            .NotEmpty()
            .WithMessage("VesselId không hợp lệ.");

        RuleFor(x => x.WaterbusServiceId)
            .NotEmpty()
            .WithMessage("Dịch vụ WaterBus không hợp lệ.");
    }
}

public sealed class AssignVesselServiceRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public AssignVesselServiceRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<VesselDto> ExecuteAsync(
        AssignVesselServiceRequest request,
        CancellationToken cancellationToken)
    {
        await VesselSupport.EnsureCurrentUserCanManageVesselsAsync(_context, _userContext, cancellationToken);

        var vessel = await _context.Vessels
            .Include(x => x.WaterbusService)
            .Include(x => x.RentalPrices)
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        var service = await _context.WaterbusServices
            .SingleOrDefaultAsync(x => x.Id == request.WaterbusServiceId, cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(request.WaterbusServiceId), "Dịch vụ WaterBus không hợp lệ.");

        if (vessel.Status == VesselStatus.Active && !service.IsActive)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.WaterbusServiceId),
                "Tàu Active chỉ được gắn với dịch vụ đang hoạt động.");
        }

        vessel.WaterbusServiceId = service.Id;
        vessel.WaterbusService = service;

        await _context.SaveChangesAsync(cancellationToken);

        return VesselSupport.CreateDto(vessel);
    }
}
