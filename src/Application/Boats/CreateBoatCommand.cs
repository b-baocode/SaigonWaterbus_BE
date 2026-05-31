using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Boats;

public sealed record CreateBoatCommand(
    string BoatCode,
    string BoatName,
    int Capacity,
    string? Description) : IRequest<BoatDto>;

public sealed class CreateBoatCommandValidator : AbstractValidator<CreateBoatCommand>
{
    public CreateBoatCommandValidator()
    {
        RuleFor(x => x.BoatCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.BoatName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Capacity).GreaterThan(0);
    }
}

public sealed class CreateBoatCommandHandler : IRequestHandler<CreateBoatCommand, BoatDto>
{
    private readonly IApplicationDbContext _context;

    public CreateBoatCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<BoatDto> Handle(CreateBoatCommand request, CancellationToken cancellationToken)
    {
        var code = request.BoatCode.Trim().ToUpperInvariant();

        if (await _context.Set<Boat>().AnyAsync(b => b.BoatCode == code, cancellationToken))
            throw new ValidationException([new ValidationFailure(nameof(request.BoatCode), "Boat code already exists.")]);

        var boat = new Boat
        {
            BoatCode = code,
            BoatName = request.BoatName.Trim(),
            Capacity = request.Capacity,
            Description = request.Description?.Trim(),
            BoatStatus = "Active"
        };

        _context.Set<Boat>().Add(boat);
        await _context.SaveChangesAsync(cancellationToken);

        return new BoatDto(boat.Id, boat.BoatCode, boat.BoatName, boat.Capacity, boat.BoatStatus, boat.Description);
    }
}
