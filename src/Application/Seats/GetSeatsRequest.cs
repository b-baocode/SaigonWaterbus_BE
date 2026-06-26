using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Boats;

namespace SaigonWaterbus.Application.Seats;

public sealed record GetSeatsRequest(Guid BoatId);

public sealed class GetSeatsRequestValidator : AbstractValidator<GetSeatsRequest>
{
    public GetSeatsRequestValidator()
    {
        RuleFor(x => x.BoatId)
            .NotEmpty()
            .WithMessage("BoatId không hợp lệ.");
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

    public async Task<BoatSeatsDto> ExecuteAsync(GetSeatsRequest request, CancellationToken cancellationToken)
    {
        var actor = await SeatSupport.EnsureCurrentUserCanViewSeatsAsync(_context, _userContext, cancellationToken);

        var boat = await BoatSupport.ApplyVisibilityFilter(
                _context.Boats
                    .AsNoTracking()
                    .AsQueryable(),
                actor)
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        var seats = await _context.Seats
            .AsNoTracking()
            .Where(x => x.BoatId == request.BoatId)
            .OrderBy(x => x.Deck)
            .ThenBy(x => x.Row)
            .ThenBy(x => x.Column)
            .ToListAsync(cancellationToken);

        return SeatSupport.CreateBoatSeatsDto(boat, seats);
    }
}
