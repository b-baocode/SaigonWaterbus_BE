using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.WaterbusServices;

public sealed record UpdateWaterbusServiceRequest(
    Guid ServiceId,
    string? Code = null,
    string? Name = null,
    string? Description = null,
    int? DisplayOrder = null,
    BookingMode? BookingMode = null);

public sealed class UpdateWaterbusServiceRequestValidator : AbstractValidator<UpdateWaterbusServiceRequest>
{
    public UpdateWaterbusServiceRequestValidator()
    {
        RuleFor(x => x.ServiceId)
            .NotEmpty()
            .WithMessage("ServiceId không hợp lệ.");

        RuleFor(x => x.Code)
            .MaximumLength(20)
            .WithMessage("Mã dịch vụ không được vượt quá 20 ký tự.")
            .Matches("^[A-Za-z0-9_]+$")
            .WithMessage("Mã dịch vụ chỉ được gồm chữ cái, số và dấu gạch dưới.")
            .When(x => !string.IsNullOrWhiteSpace(x.Code));

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Tên dịch vụ không được để trống.")
            .MaximumLength(100)
            .WithMessage("Tên dịch vụ không được vượt quá 100 ký tự.")
            .When(x => x.Name is not null);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .WithMessage("Mô tả dịch vụ không được vượt quá 500 ký tự.");

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Thứ tự hiển thị không hợp lệ.")
            .When(x => x.DisplayOrder.HasValue);

        RuleFor(x => x.BookingMode)
            .IsInEnum()
            .WithMessage("Kiểu đặt vé không hợp lệ.")
            .When(x => x.BookingMode.HasValue);
    }
}

public sealed class UpdateWaterbusServiceRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IDatabaseExceptionClassifier _databaseExceptionClassifier;

    public UpdateWaterbusServiceRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext,
        IDatabaseExceptionClassifier databaseExceptionClassifier)
    {
        _context = context;
        _userContext = userContext;
        _databaseExceptionClassifier = databaseExceptionClassifier;
    }

    public async Task<WaterbusServiceDto> ExecuteAsync(
        UpdateWaterbusServiceRequest request,
        CancellationToken cancellationToken)
    {
        await WaterbusServiceSupport.EnsureCurrentUserCanManageWaterbusServicesAsync(
            _context,
            _userContext,
            cancellationToken);

        var service = await _context.WaterbusServices
            .SingleOrDefaultAsync(x => x.Id == request.ServiceId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy dịch vụ WaterBus.");

        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            var normalizedCode = WaterbusServiceSupport.NormalizeCode(request.Code);
            if (!string.Equals(service.Code, normalizedCode, StringComparison.Ordinal)
                && await _context.WaterbusServices.AnyAsync(x => x.Code == normalizedCode, cancellationToken))
            {
                throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã dịch vụ đã tồn tại.");
            }

            service.Code = normalizedCode;
        }

        if (request.Name is not null)
        {
            service.Name = request.Name.Trim();
        }

        if (request.Description is not null)
        {
            service.Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim();
        }

        if (request.DisplayOrder.HasValue)
        {
            service.DisplayOrder = request.DisplayOrder.Value;
        }

        if (request.BookingMode.HasValue)
        {
            service.BookingMode = request.BookingMode.Value;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (_databaseExceptionClassifier.IsUniqueConstraintViolation(ex))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã dịch vụ đã tồn tại.");
        }

        return WaterbusServiceSupport.CreateDto(service);
    }
}
