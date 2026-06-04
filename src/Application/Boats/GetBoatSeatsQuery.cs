using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Boats;

public sealed record GetBoatSeatsQuery(Guid BoatId) : IRequest<IReadOnlyList<SeatDto>>;

public sealed class GetBoatSeatsQueryHandler : IRequestHandler<GetBoatSeatsQuery, IReadOnlyList<SeatDto>>
{
    private readonly IApplicationDbContext _context;

    public GetBoatSeatsQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<SeatDto>> Handle(GetBoatSeatsQuery request, CancellationToken cancellationToken)
    {
        if (!await _context.Set<Boat>().AnyAsync(b => b.Id == request.BoatId, cancellationToken))
            throw new NotFoundException("Boat not found.");

        return await _context.Set<Seat>()
            .Where(s => s.BoatId == request.BoatId)
            .OrderBy(s => s.SeatRow).ThenBy(s => s.SeatColumn)
            .Select(s => new SeatDto(s.Id, s.BoatId, s.SeatNumber, s.SeatClass,
                s.SeatRow, s.SeatColumn, s.IsActive))
            .ToListAsync(cancellationToken);
    }
}
