using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Promotions;

public sealed record ValidatePromotionQuery(string Code, decimal SubtotalAmount) : IRequest<PromotionValidationDto>;

public sealed class ValidatePromotionQueryHandler : IRequestHandler<ValidatePromotionQuery, PromotionValidationDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public ValidatePromotionQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<PromotionValidationDto> Handle(ValidatePromotionQuery request, CancellationToken cancellationToken)
    {
        var code = PromotionSupport.NormalizeCode(request.Code);

        var promotion = await _context.Set<Promotion>()
            .SingleOrDefaultAsync(p => p.PromotionCode == code, cancellationToken);

        if (promotion is null)
        {
            return new PromotionValidationDto(false, 0, "Không tìm thấy mã khuyến mãi.");
        }

        var eligibility = await PromotionEligibilitySupport.EvaluateAsync(
            _context,
            promotion,
            _userContext.UserId,
            request.SubtotalAmount,
            _timeProvider.GetUtcNow(),
            applyContext: null,
            excludedBookingId: null,
            cancellationToken);

        return eligibility.IsValid
            ? new PromotionValidationDto(true, eligibility.Discount, "Áp dụng khuyến mãi thành công.")
            : new PromotionValidationDto(false, 0, eligibility.Reason ?? "Khuyến mãi không khả dụng.");
    }
}
