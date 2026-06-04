using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.WaterbusServices;

public sealed record CreateWaterbusServiceRequest(
    string Code,
    string Name,
    string? Description = null,
    bool IsActive = true,
    int DisplayOrder = 0);

public sealed class CreateWaterbusServiceRequestValidator : AbstractValidator<CreateWaterbusServiceRequest>
{
    public CreateWaterbusServiceRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Mã dịch vụ không được để trống.")
            .MaximumLength(20)
            .WithMessage("Mã dịch vụ không được vượt quá 20 ký tự.")
            .Matches("^[A-Za-z0-9_]+$")
            .WithMessage("Mã dịch vụ chỉ được gồm chữ cái, số và dấu gạch dưới.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Tên dịch vụ không được để trống.")
            .MaximumLength(100)
            .WithMessage("Tên dịch vụ không được vượt quá 100 ký tự.");

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .WithMessage("Mô tả dịch vụ không được vượt quá 500 ký tự.");

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Thứ tự hiển thị không hợp lệ.");
    }
}

public sealed class CreateWaterbusServiceRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IDatabaseExceptionClassifier _databaseExceptionClassifier;

    public CreateWaterbusServiceRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext,
        IDatabaseExceptionClassifier databaseExceptionClassifier)
    {
        _context = context;
        _userContext = userContext;
        _databaseExceptionClassifier = databaseExceptionClassifier;
    }

    public async Task<WaterbusServiceDto> ExecuteAsync(
        CreateWaterbusServiceRequest request,
        CancellationToken cancellationToken)
    {
        await WaterbusServiceSupport.EnsureCurrentUserCanManageWaterbusServicesAsync(
            _context,
            _userContext,
            cancellationToken);

        var normalizedCode = WaterbusServiceSupport.NormalizeCode(request.Code);
        if (await _context.WaterbusServices.AnyAsync(x => x.Code == normalizedCode, cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã dịch vụ đã tồn tại.");
        }

        var service = new WaterbusService
        {
            Code = normalizedCode,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsActive = request.IsActive,
            DisplayOrder = request.DisplayOrder
        };

        try
        {
            _context.WaterbusServices.Add(service);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (_databaseExceptionClassifier.IsUniqueConstraintViolation(ex))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã dịch vụ đã tồn tại.");
        }

        return WaterbusServiceSupport.CreateDto(service);
    }
}
