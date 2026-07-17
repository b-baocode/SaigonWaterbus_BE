using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Promotions;

[Authorize(Roles = "Admin")]
public sealed record CreatePromotionCommand(
    string PromotionCode,
    string PromotionName,
    PromotionType PromotionType,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    decimal? MinOrderValue,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    int? UsageLimit,
    int? MaxUsesPerAccount,
    decimal? BudgetCap,
    bool FirstBookingOnly = false,
    PromotionScopeDto? Scope = null,
    PromotionVisibility Visibility = PromotionVisibility.Public,
    PromotionStatus Status = PromotionStatus.Draft,
    string? Description = null,
    string? ImageUrl = null,
    PromotionImageFileRequest? ImageFile = null) : IRequest<PromotionDto>;

public sealed class CreatePromotionCommandValidator : AbstractValidator<CreatePromotionCommand>
{
    public CreatePromotionCommandValidator()
    {
        RuleFor(x => x.PromotionCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PromotionName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.PromotionType).IsInEnum();
        RuleFor(x => x.Visibility).IsInEnum();
        RuleFor(x => x.Status).IsInEnum()
            .NotEqual(PromotionStatus.Archived).WithMessage("Không thể tạo mới với trạng thái Archived.");
        RuleFor(x => x.DiscountValue).GreaterThan(0);
        RuleFor(x => x.DiscountValue)
            .LessThanOrEqualTo(100)
            .When(x => x.PromotionType == PromotionType.Percent)
            .WithMessage("Percent discount không được vượt quá 100.");
        RuleFor(x => x.MaxDiscountAmount).GreaterThan(0).When(x => x.MaxDiscountAmount.HasValue);
        RuleFor(x => x.MinOrderValue).GreaterThanOrEqualTo(0).When(x => x.MinOrderValue.HasValue);
        RuleFor(x => x.BudgetCap).GreaterThan(0).When(x => x.BudgetCap.HasValue);
        RuleFor(x => x.UsageLimit).GreaterThan(0).When(x => x.UsageLimit.HasValue);
        RuleFor(x => x.MaxUsesPerAccount).GreaterThan(0).When(x => x.MaxUsesPerAccount.HasValue);
        RuleFor(x => x.ValidTo).GreaterThan(x => x.ValidFrom);
    }
}

public sealed class CreatePromotionCommandHandler : IRequestHandler<CreatePromotionCommand, PromotionDto>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly IPromotionImageStorageService? _imageStorage;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;

    public CreatePromotionCommandHandler(
        IApplicationDbContext context,
        TimeProvider timeProvider,
        IPromotionImageStorageService? imageStorage = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _timeProvider = timeProvider;
        _imageStorage = imageStorage;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
    }

    public async Task<PromotionDto> Handle(CreatePromotionCommand request, CancellationToken cancellationToken)
    {
        var code = PromotionSupport.NormalizeCode(request.PromotionCode);

        if (request.PromotionType == PromotionType.Fixed && request.MaxDiscountAmount.HasValue)
        {
            throw PromotionSupport.Fail(nameof(request.MaxDiscountAmount),
                "MaxDiscountAmount chỉ áp dụng cho loại Percent.");
        }

        if (await _context.Set<Promotion>().AnyAsync(p => p.PromotionCode == code, cancellationToken))
        {
            throw PromotionSupport.Fail(nameof(request.PromotionCode), "Mã khuyến mãi đã tồn tại.");
        }

        var scope = await PromotionSupport.NormalizeScopeAsync(
            _context, request.Scope, nameof(request.Scope), cancellationToken);

        var promotion = new Promotion
        {
            PromotionCode = code,
            PromotionName = request.PromotionName.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            PromotionType = request.PromotionType,
            DiscountValue = request.DiscountValue,
            MaxDiscountAmount = request.MaxDiscountAmount,
            MinOrderValue = request.MinOrderValue,
            ValidFrom = request.ValidFrom.ToUniversalTime(),
            ValidTo = request.ValidTo.ToUniversalTime(),
            UsageLimit = request.UsageLimit,
            MaxUsesPerAccount = request.MaxUsesPerAccount,
            BudgetCap = request.BudgetCap,
            FirstBookingOnly = request.FirstBookingOnly,
            Scope = scope,
            Visibility = request.Visibility,
            Status = request.Status
        };

        if (request.ImageFile is not null)
        {
            var stored = await PromotionSupport.UploadImageAsync(
                promotion.Id, request.ImageFile, _imageStorage, nameof(request.ImageFile), cancellationToken);
            promotion.ImageUrl = stored.Url;
            promotion.ImagePublicId = stored.PublicId;
        }
        else
        {
            promotion.ImageUrl = PromotionSupport.NormalizeImageUrl(request.ImageUrl, nameof(request.ImageUrl));
        }

        _context.Set<Promotion>().Add(promotion);
        var announcementNotifications = await NotificationSupport.AddPromotionAnnouncementNotificationsAsync(
            _context, promotion, _timeProvider.GetUtcNow(), cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await NotificationSupport.PublishCreatedAsync(
            _notificationRealtimeNotifier, announcementNotifications, cancellationToken);

        return PromotionSupport.ToDto(promotion, _timeProvider.GetUtcNow(), 0, 0);
    }
}
