using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Vessels;

public sealed record GetVesselByIdRequest(Guid VesselId);

public sealed class GetVesselByIdRequestValidator : AbstractValidator<GetVesselByIdRequest>
{
    public GetVesselByIdRequestValidator()
    {
        RuleFor(x => x.VesselId)
            .NotEmpty()
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
                    .Include(x => x.Images)
                    .Include(x => x.RentalPrices)
                    .AsQueryable(),
                actor)
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        return VesselSupport.CreateDto(vessel);
    }
}
