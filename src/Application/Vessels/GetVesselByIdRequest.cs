using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Vessels;

public sealed record GetVesselByIdRequest(int VesselId);

public sealed class GetVesselByIdRequestValidator : AbstractValidator<GetVesselByIdRequest>
{
    public GetVesselByIdRequestValidator()
    {
        RuleFor(x => x.VesselId)
            .GreaterThan(0)
            .WithMessage("VesselId không hợp lệ.");
    }
}

public sealed class GetVesselByIdRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetVesselByIdRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<VesselDto> ExecuteAsync(
        GetVesselByIdRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await VesselSupport.EnsureCurrentUserCanViewVesselsAsync(_context, _userContext, cancellationToken);
        var vessel = await VesselSupport.ApplyVisibilityFilter(
                _context.Vessels
                    .AsNoTracking()
                    .Include(x => x.WaterbusService)
                    .AsQueryable(),
                actor)
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        var generatedSeatCount = await _context.Seats
            .AsNoTracking()
            .CountAsync(s => s.VesselId == vessel.Id, cancellationToken);

        return VesselSupport.CreateDto(vessel, generatedSeatCount);
    }
}
