using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.TicketTypes;

public sealed record CreateTicketTypeCommand(
    string TicketTypeCode,
    string TicketTypeName,
    string? Description,
    decimal PriceModifier,
    int PointsEarnedRate) : IRequest<TicketTypeDto>;

public sealed class CreateTicketTypeCommandValidator : AbstractValidator<CreateTicketTypeCommand>
{
    public CreateTicketTypeCommandValidator()
    {
        RuleFor(x => x.TicketTypeCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.TicketTypeName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PriceModifier).GreaterThan(0);
        RuleFor(x => x.PointsEarnedRate).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateTicketTypeCommandHandler : IRequestHandler<CreateTicketTypeCommand, TicketTypeDto>
{
    private readonly IApplicationDbContext _context;

    public CreateTicketTypeCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<TicketTypeDto> Handle(CreateTicketTypeCommand request, CancellationToken cancellationToken)
    {
        var code = request.TicketTypeCode.Trim().ToUpperInvariant();

        if (await _context.Set<TicketType>().AnyAsync(tt => tt.TicketTypeCode == code, cancellationToken))
            throw new ValidationException([new ValidationFailure(nameof(request.TicketTypeCode), "Ticket type code already exists.")]);

        var ticketType = new TicketType
        {
            TicketTypeCode = code,
            TicketTypeName = request.TicketTypeName.Trim(),
            Description = request.Description?.Trim(),
            PriceModifier = request.PriceModifier,
            PointsEarnedRate = request.PointsEarnedRate,
            IsActive = true
        };

        _context.Set<TicketType>().Add(ticketType);
        await _context.SaveChangesAsync(cancellationToken);

        return new TicketTypeDto(ticketType.Id, ticketType.TicketTypeCode, ticketType.TicketTypeName,
            ticketType.Description, ticketType.PriceModifier, ticketType.PointsEarnedRate, ticketType.IsActive);
    }
}
