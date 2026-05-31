using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Boats;

public sealed record CreateSeatsCommand(
    Guid BoatId,
    int Rows,
    int Columns,
    string? SeatClass) : IRequest<CreateSeatsResult>;

public sealed record CreateSeatsResult(Guid BoatId, int SeatsCreated);

public sealed class CreateSeatsCommandValidator : AbstractValidator<CreateSeatsCommand>
{
    public CreateSeatsCommandValidator()
    {
        RuleFor(x => x.BoatId).NotEmpty();
        RuleFor(x => x.Rows).InclusiveBetween(1, 20);
        RuleFor(x => x.Columns).InclusiveBetween(1, 10);
    }
}

public sealed class CreateSeatsCommandHandler : IRequestHandler<CreateSeatsCommand, CreateSeatsResult>
{
    private readonly IApplicationDbContext _context;

    public CreateSeatsCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<CreateSeatsResult> Handle(CreateSeatsCommand request, CancellationToken cancellationToken)
    {
        if (!await _context.Set<Boat>().AnyAsync(b => b.Id == request.BoatId, cancellationToken))
            throw new NotFoundException("Boat not found.");

        var existingNumbers = await _context.Set<Seat>()
            .Where(s => s.BoatId == request.BoatId)
            .Select(s => s.SeatNumber)
            .ToHashSetAsync(cancellationToken);

        var seats = new List<Seat>();
        for (var row = 0; row < request.Rows; row++)
        {
            var rowLabel = (char)('A' + row);
            for (var col = 1; col <= request.Columns; col++)
            {
                var number = $"{rowLabel}{col}";
                if (existingNumbers.Contains(number)) continue;

                seats.Add(new Seat
                {
                    BoatId = request.BoatId,
                    SeatNumber = number,
                    SeatClass = request.SeatClass,
                    SeatRow = row + 1,
                    SeatColumn = col,
                    IsActive = true
                });
            }
        }

        _context.Set<Seat>().AddRange(seats);
        await _context.SaveChangesAsync(cancellationToken);

        return new CreateSeatsResult(request.BoatId, seats.Count);
    }
}
