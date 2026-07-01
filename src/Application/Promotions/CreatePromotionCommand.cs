using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Promotions;

public sealed record CreatePromotionCommand(
    string PromotionCode,
    string PromotionName,
    PromotionType PromotionType,
    decimal DiscountValue,
    decimal? MinOrderValue,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    int? UsageLimit,
    PromotionAccountUsagePolicy AccountUsagePolicy = PromotionAccountUsagePolicy.MultiplePerAccount) : IRequest<PromotionDto>;

public sealed class CreatePromotionCommandValidator : AbstractValidator<CreatePromotionCommand>
{
    public CreatePromotionCommandValidator()
    {
        RuleFor(x => x.PromotionCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PromotionName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.PromotionType).IsInEnum();
        RuleFor(x => x.AccountUsagePolicy).IsInEnum();
        RuleFor(x => x.DiscountValue).GreaterThan(0);
        RuleFor(x => x.DiscountValue)
            .LessThanOrEqualTo(100)
            .When(x => x.PromotionType == PromotionType.Percent)
            .WithMessage("Percent discount cannot exceed 100.");
        RuleFor(x => x.ValidTo).GreaterThan(x => x.ValidFrom);
        RuleFor(x => x.UsageLimit).GreaterThan(0).When(x => x.UsageLimit.HasValue);
    }
}

public sealed class CreatePromotionCommandHandler : IRequestHandler<CreatePromotionCommand, PromotionDto>
{
    private readonly IApplicationDbContext _context;

    public CreatePromotionCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<PromotionDto> Handle(CreatePromotionCommand request, CancellationToken cancellationToken)
    {
        var code = request.PromotionCode.Trim().ToUpperInvariant();
        if (request.PromotionType == PromotionType.Percent && request.DiscountValue > 100)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.DiscountValue),
                "Percent discount cannot exceed 100.")]);
        }

        if (request.ValidTo <= request.ValidFrom)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.ValidTo),
                "ValidTo must be greater than ValidFrom.")]);
        }

        if (request.UsageLimit.HasValue && request.UsageLimit <= 0)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.UsageLimit),
                "UsageLimit must be greater than 0.")]);
        }

        if (await _context.Set<Promotion>().AnyAsync(p => p.PromotionCode == code, cancellationToken))
            throw new ValidationException([new ValidationFailure(nameof(request.PromotionCode), "Promotion code already exists.")]);

        var promotion = new Promotion
        {
            PromotionCode = code,
            PromotionName = request.PromotionName.Trim(),
            PromotionType = request.PromotionType,
            DiscountValue = request.DiscountValue,
            MinOrderValue = request.MinOrderValue,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            UsageLimit = request.UsageLimit,
            UsageCount = 0,
            AccountUsagePolicy = request.AccountUsagePolicy,
            Status = "Active"
        };

        _context.Set<Promotion>().Add(promotion);
        await _context.SaveChangesAsync(cancellationToken);

        return ToDto(promotion);
    }

    private static PromotionDto ToDto(Promotion p) => new(
        p.Id, p.PromotionCode, p.PromotionName, p.PromotionType,
        p.DiscountValue, p.MinOrderValue, p.ValidFrom, p.ValidTo,
        p.UsageLimit, p.UsageCount, p.AccountUsagePolicy, p.Status);
}
