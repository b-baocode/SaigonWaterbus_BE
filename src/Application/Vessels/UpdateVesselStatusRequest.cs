using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Vessels;

public sealed record UpdateVesselStatusRequest(
    int VesselId,
    VesselStatus Status);

public sealed class UpdateVesselStatusRequestValidator : AbstractValidator<UpdateVesselStatusRequest>
{
    public UpdateVesselStatusRequestValidator()
    {
        RuleFor(x => x.VesselId)
            .GreaterThan(0)
            .WithMessage("VesselId không hợp lệ.");

        RuleFor(x => x.Status)
            .IsInEnum()
            .WithMessage("Trạng thái tàu không hợp lệ.");
    }
}

public sealed class UpdateVesselStatusRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public UpdateVesselStatusRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<VesselDto> ExecuteAsync(
        UpdateVesselStatusRequest request,
        CancellationToken cancellationToken)
    {
        await VesselSupport.EnsureCurrentUserCanManageVesselsAsync(_context, _userContext, cancellationToken);

        var vessel = await _context.Vessels
            .Include(x => x.WaterbusService)
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        vessel.Status = request.Status;
        await _context.SaveChangesAsync(cancellationToken);

        return VesselSupport.CreateDto(vessel);
    }
}
