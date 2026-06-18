using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Vessels;

public sealed record UpdateVesselStatusRequest(
    Guid VesselId,
    VesselStatus Status);

public sealed class UpdateVesselStatusRequestValidator : AbstractValidator<UpdateVesselStatusRequest>
{
    public UpdateVesselStatusRequestValidator()
    {
        RuleFor(x => x.VesselId)
            .NotEmpty()
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
            .Include(x => x.Images)
            .Include(x => x.RentalPrices)
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        if (request.Status == VesselStatus.Active)
        {
            VesselSupport.EnsureCanActivate(vessel, nameof(request.Status));
        }

        vessel.Status = request.Status;
        await _context.SaveChangesAsync(cancellationToken);

        return VesselSupport.CreateDto(vessel);
    }
}
