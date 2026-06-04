using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Boats;

public sealed record BoatDetailDto(
    Guid BoatId,
    string BoatCode,
    string BoatName,
    int Capacity,
    string BoatStatus,
    string? Description,
    int TotalSeats,
    int ActiveSeats);

public sealed record GetBoatDetailQuery(Guid BoatId) : IRequest<BoatDetailDto>;

public sealed class GetBoatDetailQueryHandler : IRequestHandler<GetBoatDetailQuery, BoatDetailDto>
{
    private readonly IApplicationDbContext _context;

    public GetBoatDetailQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<BoatDetailDto> Handle(GetBoatDetailQuery request, CancellationToken cancellationToken)
    {
        var boat = await _context.Set<Boat>()
            .Include(b => b.Seats)
            .SingleOrDefaultAsync(b => b.Id == request.BoatId, cancellationToken)
            ?? throw new NotFoundException("Boat not found.");

        return new BoatDetailDto(
            boat.Id, boat.BoatCode, boat.BoatName, boat.Capacity,
            boat.BoatStatus, boat.Description,
            boat.Seats.Count,
            boat.Seats.Count(s => s.IsActive));
    }
}
