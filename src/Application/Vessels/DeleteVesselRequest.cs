using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Vessels;

public sealed record DeleteVesselRequest(int VesselId);

public sealed class DeleteVesselRequestValidator : AbstractValidator<DeleteVesselRequest>
{
    public DeleteVesselRequestValidator()
    {
        RuleFor(x => x.VesselId)
            .GreaterThan(0)
            .WithMessage("VesselId không hợp lệ.");
    }
}

public sealed class DeleteVesselRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public DeleteVesselRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<AuthActionResultDto> ExecuteAsync(
        DeleteVesselRequest request,
        CancellationToken cancellationToken)
    {
        await VesselSupport.EnsureCurrentUserCanManageVesselsAsync(_context, _userContext, cancellationToken);

        var vessel = await _context.Vessels
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        _context.Vessels.Remove(vessel);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthActionResultDto("Xóa tàu thành công.");
    }
}
