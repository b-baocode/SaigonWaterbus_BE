using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.WaterbusServices;

public sealed record UpdateWaterbusServiceSeatPriceRequest(
    Guid ServiceId,
    string SeatTypeCode,
    decimal PriceModifier,
    bool IsActive);

public sealed class UpdateWaterbusServiceSeatPriceRequestValidator
    : AbstractValidator<UpdateWaterbusServiceSeatPriceRequest>
{
    public UpdateWaterbusServiceSeatPriceRequestValidator()
    {
        RuleFor(x => x.ServiceId)
            .NotEmpty()
            .WithMessage("ServiceId không hợp lệ.");

        RuleFor(x => x.SeatTypeCode)
            .NotEmpty()
            .MaximumLength(30)
            .WithMessage("Mã loại ghế không hợp lệ.");

        RuleFor(x => x.PriceModifier)
            .GreaterThan(0)
            .LessThanOrEqualTo(10)
            .WithMessage("Hệ số giá phải lớn hơn 0 và không vượt quá 10.");
    }
}

public sealed class UpdateWaterbusServiceSeatPriceRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public UpdateWaterbusServiceSeatPriceRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<WaterbusServiceSeatTypesDto> ExecuteAsync(
        UpdateWaterbusServiceSeatPriceRequest request,
        CancellationToken cancellationToken)
    {
        await WaterbusServiceSupport.EnsureCurrentUserCanManageWaterbusServicesAsync(
            _context,
            _userContext,
            cancellationToken);

        var service = await _context.WaterbusServices
            .Include(x => x.SeatTypePrices)
                .ThenInclude(x => x.SeatType)
            .SingleOrDefaultAsync(x => x.Id == request.ServiceId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException(
                "Không tìm thấy dịch vụ WaterBus.");

        var normalizedCode = request.SeatTypeCode.Trim().ToUpperInvariant();
        var seatType = await _context.SeatTypes
            .SingleOrDefaultAsync(x => x.Code == normalizedCode, cancellationToken)
            ?? throw AuthSupport.CreateValidationException(
                nameof(request.SeatTypeCode),
                $"Loại ghế '{normalizedCode}' không tồn tại.");

        if (request.IsActive && !seatType.IsActive)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.SeatTypeCode),
                $"Loại ghế '{normalizedCode}' đang bị vô hiệu hóa.");
        }

        var price = service.SeatTypePrices
            .SingleOrDefault(x => x.SeatTypeId == seatType.Id);
        if (price is null)
        {
            price = WaterbusServiceSupport.CreateServiceSeatTypePrice(
                service,
                seatType,
                request.PriceModifier);
            _context.ServiceSeatTypePrices.Add(price);
        }
        else
        {
            price.PriceModifier = request.PriceModifier;
            price.IsActive = request.IsActive;
        }

        price.IsActive = request.IsActive;
        await _context.SaveChangesAsync(cancellationToken);

        var availableSeatTypes = await _context.SeatTypes
            .AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Code)
            .ToArrayAsync(cancellationToken);

        return WaterbusServiceSupport.CreateSeatTypesDto(
            service,
            includeInactive: true,
            availableSeatTypes: availableSeatTypes);
    }
}
