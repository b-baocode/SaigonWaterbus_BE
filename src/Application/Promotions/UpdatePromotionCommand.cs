using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Promotions;

public sealed record UpdatePromotionCommand(
    Guid PromotionId,
    string PromotionName,
    decimal DiscountValue,
    decimal? MinOrderValue,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    int? UsageLimit,
    PromotionAccountUsagePolicy AccountUsagePolicy,
    string Status) : IRequest<PromotionDto>;

public sealed class UpdatePromotionCommandValidator : AbstractValidator<UpdatePromotionCommand>
{
    public UpdatePromotionCommandValidator()
    {
        RuleFor(x => x.PromotionId).NotEmpty();
        RuleFor(x => x.PromotionName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DiscountValue).GreaterThan(0);
        RuleFor(x => x.ValidTo).GreaterThan(x => x.ValidFrom);
        RuleFor(x => x.UsageLimit).GreaterThan(0).When(x => x.UsageLimit.HasValue);
        RuleFor(x => x.AccountUsagePolicy).IsInEnum();
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(status => string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Inactive", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Status hop le: Active | Inactive.");
    }
}

public sealed class UpdatePromotionCommandHandler : IRequestHandler<UpdatePromotionCommand, PromotionDto>
{
    private readonly IApplicationDbContext _context;

    public UpdatePromotionCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<PromotionDto> Handle(UpdatePromotionCommand request, CancellationToken cancellationToken)
    {
        var promotion = await _context.Set<Promotion>()
            .SingleOrDefaultAsync(p => p.Id == request.PromotionId, cancellationToken)
            ?? throw new NotFoundException("Promotion not found.");

        if (promotion.PromotionType == Domain.Enums.PromotionType.Percent && request.DiscountValue > 100)
        {
            throw new SaigonWaterbus.Application.Common.Exceptions.ValidationException(
                [new FluentValidation.Results.ValidationFailure(
                    nameof(request.DiscountValue),
                    "Percent discount cannot exceed 100.")]);
        }

        if (request.ValidTo <= request.ValidFrom)
        {
            throw new SaigonWaterbus.Application.Common.Exceptions.ValidationException(
                [new FluentValidation.Results.ValidationFailure(
                    nameof(request.ValidTo),
                    "ValidTo must be greater than ValidFrom.")]);
        }

        if (request.UsageLimit.HasValue && request.UsageLimit <= 0)
        {
            throw new SaigonWaterbus.Application.Common.Exceptions.ValidationException(
                [new FluentValidation.Results.ValidationFailure(
                    nameof(request.UsageLimit),
                    "UsageLimit must be greater than 0.")]);
        }

        promotion.PromotionName = request.PromotionName.Trim();
        promotion.DiscountValue = request.DiscountValue;
        promotion.MinOrderValue = request.MinOrderValue;
        promotion.ValidFrom = request.ValidFrom;
        promotion.ValidTo = request.ValidTo;
        promotion.UsageLimit = request.UsageLimit;
        promotion.AccountUsagePolicy = request.AccountUsagePolicy;
        promotion.Status = NormalizeStatus(request.Status);

        await _context.SaveChangesAsync(cancellationToken);

        return new PromotionDto(promotion.Id, promotion.PromotionCode, promotion.PromotionName,
            promotion.PromotionType, promotion.DiscountValue, promotion.MinOrderValue,
            promotion.ValidFrom, promotion.ValidTo, promotion.UsageLimit, promotion.UsageCount,
            promotion.AccountUsagePolicy, promotion.Status);
    }

    private static string NormalizeStatus(string status) =>
        string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
            ? "Active"
            : string.Equals(status, "Inactive", StringComparison.OrdinalIgnoreCase)
                ? "Inactive"
                : throw new SaigonWaterbus.Application.Common.Exceptions.ValidationException(
                    [new FluentValidation.Results.ValidationFailure(
                        nameof(UpdatePromotionCommand.Status),
                        "Status hop le: Active | Inactive.")]);
}
