using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Vessels;

namespace SaigonWaterbus.Application.Seats;

public sealed record GetSeatsRequest(int VesselId);

public sealed class GetSeatsRequestValidator : AbstractValidator<GetSeatsRequest>
{
    public GetSeatsRequestValidator()
    {
        RuleFor(x => x.VesselId)
            .GreaterThan(0)
            .WithMessage("VesselId không hợp lệ.");
    }
}

public sealed class GetSeatsRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetSeatsRequestUseCase(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<VesselSeatsDto> ExecuteAsync(GetSeatsRequest request, CancellationToken cancellationToken)
    {
        var actor = await SeatSupport.EnsureCurrentUserCanViewSeatsAsync(_context, _userContext, cancellationToken);

        var vessel = await VesselSupport.ApplyVisibilityFilter(
                _context.Vessels
                    .AsNoTracking()
                    .AsQueryable(),
                actor)
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        var seats = await _context.Seats
            .AsNoTracking()
            .Where(x => x.VesselId == request.VesselId)
            .OrderBy(x => x.Deck)
            .ThenBy(x => x.Row)
            .ThenBy(x => x.Column)
            .ToListAsync(cancellationToken);

        return SeatSupport.CreateVesselSeatsDto(vessel, seats);
    }
}
