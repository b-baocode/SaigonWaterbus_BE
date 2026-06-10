using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.TicketTypes;

public sealed record UpdateTicketTypeCommand(
    Guid TicketTypeId,
    string TicketTypeName,
    string? Description,
    decimal PriceModifier,
    int PointsEarnedRate,
    bool IsActive) : IRequest<TicketTypeDto>;

public sealed class UpdateTicketTypeCommandValidator : AbstractValidator<UpdateTicketTypeCommand>
{
    public UpdateTicketTypeCommandValidator()
    {
        RuleFor(x => x.TicketTypeId).NotEmpty();
        RuleFor(x => x.TicketTypeName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PriceModifier).GreaterThan(0);
    }
}

public sealed class UpdateTicketTypeCommandHandler : IRequestHandler<UpdateTicketTypeCommand, TicketTypeDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateTicketTypeCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<TicketTypeDto> Handle(UpdateTicketTypeCommand request, CancellationToken cancellationToken)
    {
        var tt = await _context.Set<TicketType>()
            .SingleOrDefaultAsync(x => x.Id == request.TicketTypeId, cancellationToken)
            ?? throw new NotFoundException("Ticket type not found.");

        tt.TicketTypeName = request.TicketTypeName.Trim();
        tt.Description = request.Description?.Trim();
        tt.PriceModifier = request.PriceModifier;
        tt.PointsEarnedRate = request.PointsEarnedRate;
        tt.IsActive = request.IsActive;

        await _context.SaveChangesAsync(cancellationToken);

        return new TicketTypeDto(tt.Id, tt.TicketTypeCode, tt.TicketTypeName,
            tt.Description, tt.PriceModifier, tt.PointsEarnedRate, tt.IsActive);
    }
}
