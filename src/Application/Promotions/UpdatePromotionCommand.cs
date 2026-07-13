using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Promotions;

[Authorize(Roles = "Admin")]
public sealed record UpdatePromotionCommand(
    Guid PromotionId,
    string PromotionName,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    decimal? MinOrderValue,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    int? UsageLimit,
    int? MaxUsesPerAccount,
    decimal? BudgetCap,
    bool FirstBookingOnly,
    PromotionScopeDto? Scope,
    PromotionVisibility Visibility,
    PromotionStatus Status,
    string? Description = null) : IRequest<PromotionDto>;

public sealed class UpdatePromotionCommandValidator : AbstractValidator<UpdatePromotionCommand>
{
    public UpdatePromotionCommandValidator()
    {
        RuleFor(x => x.PromotionId).NotEmpty();
        RuleFor(x => x.PromotionName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.DiscountValue).GreaterThan(0);
        RuleFor(x => x.MaxDiscountAmount).GreaterThan(0).When(x => x.MaxDiscountAmount.HasValue);
        RuleFor(x => x.MinOrderValue).GreaterThanOrEqualTo(0).When(x => x.MinOrderValue.HasValue);
        RuleFor(x => x.BudgetCap).GreaterThan(0).When(x => x.BudgetCap.HasValue);
        RuleFor(x => x.UsageLimit).GreaterThan(0).When(x => x.UsageLimit.HasValue);
        RuleFor(x => x.MaxUsesPerAccount).GreaterThan(0).When(x => x.MaxUsesPerAccount.HasValue);
        RuleFor(x => x.ValidTo).GreaterThan(x => x.ValidFrom);
        RuleFor(x => x.Visibility).IsInEnum();
        RuleFor(x => x.Status).IsInEnum();
    }
}

public sealed class UpdatePromotionCommandHandler : IRequestHandler<UpdatePromotionCommand, PromotionDto>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public UpdatePromotionCommandHandler(IApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<PromotionDto> Handle(UpdatePromotionCommand request, CancellationToken cancellationToken)
    {
        var promotion = await _context.Set<Promotion>()
            .SingleOrDefaultAsync(p => p.Id == request.PromotionId, cancellationToken)
            ?? throw new NotFoundException("Promotion not found.");

        // PromotionType cố định sau khi tạo — MaxDiscountAmount chỉ hợp lệ với Percent.
        if (promotion.PromotionType == PromotionType.Percent && request.DiscountValue > 100)
        {
            throw PromotionSupport.Fail(nameof(request.DiscountValue), "Percent discount không được vượt quá 100.");
        }

        if (promotion.PromotionType == PromotionType.Fixed && request.MaxDiscountAmount.HasValue)
        {
            throw PromotionSupport.Fail(nameof(request.MaxDiscountAmount), "MaxDiscountAmount chỉ áp dụng cho loại Percent.");
        }

        var (totalUsed, budgetSpent) = await PromotionSupport.GetUsageAsync(_context, promotion.Id, cancellationToken);

        if (request.UsageLimit.HasValue && request.UsageLimit.Value < totalUsed)
        {
            throw PromotionSupport.Fail(nameof(request.UsageLimit),
                $"Không thể đặt UsageLimit ({request.UsageLimit}) nhỏ hơn số lượt đã dùng ({totalUsed}).");
        }

        if (request.BudgetCap.HasValue && request.BudgetCap.Value < budgetSpent)
        {
            throw PromotionSupport.Fail(nameof(request.BudgetCap),
                $"Không thể đặt BudgetCap ({request.BudgetCap:N0}) nhỏ hơn ngân sách đã tiêu ({budgetSpent:N0}).");
        }

        var scope = await PromotionSupport.NormalizeScopeAsync(
            _context, request.Scope, nameof(request.Scope), cancellationToken);

        promotion.PromotionName = request.PromotionName.Trim();
        promotion.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        promotion.DiscountValue = request.DiscountValue;
        promotion.MaxDiscountAmount = request.MaxDiscountAmount;
        promotion.MinOrderValue = request.MinOrderValue;
        promotion.ValidFrom = request.ValidFrom.ToUniversalTime();
        promotion.ValidTo = request.ValidTo.ToUniversalTime();
        promotion.UsageLimit = request.UsageLimit;
        promotion.MaxUsesPerAccount = request.MaxUsesPerAccount;
        promotion.BudgetCap = request.BudgetCap;
        promotion.FirstBookingOnly = request.FirstBookingOnly;
        promotion.Scope = scope;
        promotion.Visibility = request.Visibility;
        promotion.Status = request.Status;

        await _context.SaveChangesAsync(cancellationToken);

        return PromotionSupport.ToDto(promotion, _timeProvider.GetUtcNow(), totalUsed, budgetSpent);
    }
}
