using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.TicketTypes;

public sealed record DeleteTicketTypeCommand(Guid TicketTypeId) : IRequest;

public sealed class DeleteTicketTypeCommandValidator : AbstractValidator<DeleteTicketTypeCommand>
{
    public DeleteTicketTypeCommandValidator()
    {
        RuleFor(x => x.TicketTypeId).NotEmpty();
    }
}

public sealed class DeleteTicketTypeCommandHandler : IRequestHandler<DeleteTicketTypeCommand>
{
    private readonly IApplicationDbContext _context;

    public DeleteTicketTypeCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task Handle(DeleteTicketTypeCommand request, CancellationToken cancellationToken)
    {
        var ticketType = await _context.Set<TicketType>()
            .SingleOrDefaultAsync(t => t.Id == request.TicketTypeId, cancellationToken)
            ?? throw new NotFoundException("Ticket type not found.");

        ticketType.IsActive = false;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
