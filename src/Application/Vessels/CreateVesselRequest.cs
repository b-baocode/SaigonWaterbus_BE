using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Vessels;

public sealed record CreateVesselRequest(
    string Code,
    string Name,
    VesselStatus Status,
    int SeatCount,
    int NumberOfDecks,
    string? RegistrationNumber = null,
    int? MaxSpeedKmh = null,
    int? YearBuilt = null,
    string? Description = null,
    string? ImageUrl = null,
    string? ImageFileName = null,
    string? ImageContentType = null,
    long? ImageLength = null,
    Stream? ImageContent = null,
    SeatSetupType SeatSetupType = SeatSetupType.FullStandard,
    IReadOnlyCollection<VesselRentalPriceRequest>? RentalPrices = null,
    IReadOnlyCollection<string>? ImageUrls = null,
    IReadOnlyCollection<VesselImageFileRequest>? ImageFiles = null);

public sealed class CreateVesselRequestValidator : AbstractValidator<CreateVesselRequest>
{
    public CreateVesselRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Mã tàu không được để trống.")
            .MaximumLength(50)
            .WithMessage("Mã tàu không được vượt quá 50 ký tự.")
            .Matches("^[A-Za-z0-9_]+$")
            .WithMessage("Mã tàu chỉ được gồm chữ cái, số và dấu gạch dưới.");

        RuleFor(x => x.RegistrationNumber)
            .MaximumLength(100)
            .WithMessage("Số đăng ký tàu không được vượt quá 100 ký tự.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Tên tàu không được để trống.")
            .MaximumLength(150)
            .WithMessage("Tên tàu không được vượt quá 150 ký tự.");

        RuleFor(x => x.Status)
            .IsInEnum()
            .WithMessage("Trạng thái tàu không hợp lệ.");

        RuleFor(x => x.SeatCount)
            .GreaterThan(0)
            .WithMessage("Số ghế phải lớn hơn 0.");

        RuleFor(x => x.NumberOfDecks)
            .GreaterThan(0)
            .WithMessage("Số tầng phải lớn hơn 0.");

        RuleFor(x => x.SeatSetupType)
            .IsInEnum()
            .WithMessage("Kiểu ghế của tàu không hợp lệ.");

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .WithMessage("Mô tả tàu không được vượt quá 1000 ký tự.");

        RuleFor(x => x.ImageUrl)
            .MaximumLength(1000)
            .WithMessage("Đường dẫn ảnh không được vượt quá 1000 ký tự.")
            .Must(VesselSupport.IsValidImageUrl)
            .WithMessage("Đường dẫn ảnh không hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.ImageUrl));

        RuleForEach(x => x.ImageUrls)
            .MaximumLength(1000)
            .WithMessage("Đường dẫn ảnh không được vượt quá 1000 ký tự.")
            .Must(VesselSupport.IsValidImageUrl)
            .WithMessage("Đường dẫn ảnh không hợp lệ.");

        RuleFor(x => x)
            .Must(x => VesselSupport.HasValidRequestedImageCount(
                x.ImageUrl,
                x.ImageUrls,
                x.ImageContent,
                x.ImageFiles))
            .WithMessage("Schema gọn chỉ lưu 1 ảnh chính cho mỗi tàu.");

        RuleFor(x => x.RentalPrices)
            .Must(VesselSupport.HasDistinctRentalUnits)
            .WithMessage("Mỗi đơn vị thuê tàu chỉ được cấu hình một giá.");

        RuleForEach(x => x.RentalPrices)
            .ChildRules(price =>
            {
                price.RuleFor(x => x.RentalUnit)
                    .IsInEnum()
                    .WithMessage("Đơn vị thuê tàu chỉ được là Hour hoặc Day.");

                price.RuleFor(x => x.UnitPrice)
                    .GreaterThan(0)
                    .WithMessage("Giá thuê tàu phải lớn hơn 0.");

                price.RuleFor(x => x.Currency)
                    .Must(VesselSupport.IsValidCurrencyCode)
                    .WithMessage("Currency phải là mã ISO 4217 gồm 3 chữ cái, ví dụ VND.")
                    .When(x => x.Currency is not null);
            });
    }
}

public sealed class CreateVesselRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IDatabaseExceptionClassifier _databaseExceptionClassifier;

    public CreateVesselRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext,
        IDatabaseExceptionClassifier databaseExceptionClassifier,
        IVesselImageStorageService? vesselImageStorageService = null)
    {
        _context = context;
        _userContext = userContext;
        _databaseExceptionClassifier = databaseExceptionClassifier;
    }

    public async Task<VesselDto> ExecuteAsync(
        CreateVesselRequest request,
        CancellationToken cancellationToken)
    {
        await VesselSupport.EnsureCurrentUserCanManageVesselsAsync(_context, _userContext, cancellationToken);

        if (request.Status == VesselStatus.Active)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.Status),
                "Tạo tàu mới cần để Inactive, cấu hình đủ ghế xong mới chuyển Active.");
        }

        if (request.ImageContent is not null || request.ImageFiles is { Count: > 0 })
        {
            throw AuthSupport.CreateValidationException(nameof(request.ImageContent), "Schema gọn chỉ hỗ trợ imageUrl, không lưu nhiều ảnh tàu.");
        }

        var normalizedCode = VesselSupport.NormalizeCode(request.Code);
        if (await _context.Vessels.AnyAsync(x => x.Code == normalizedCode, cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã tàu đã tồn tại.");
        }

        var normalizedRegistrationNumber = VesselSupport.NormalizeRegistrationNumber(request.RegistrationNumber);
        if (normalizedRegistrationNumber is not null
            && await _context.Vessels.AnyAsync(x => x.RegistrationNumber == normalizedRegistrationNumber, cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(request.RegistrationNumber), "Số đăng ký tàu đã tồn tại.");
        }

        var imageUrl = VesselSupport.NormalizeImageUrls(request.ImageUrl, request.ImageUrls).FirstOrDefault();
        var vessel = new Vessel
        {
            Code = normalizedCode,
            RegistrationNumber = normalizedRegistrationNumber,
            Name = request.Name.Trim(),
            Status = request.Status,
            SeatCount = request.SeatCount,
            NumberOfDecks = request.NumberOfDecks,
            SeatSetupType = request.SeatSetupType,
            ImageUrl = imageUrl,
            MaxSpeedKmh = request.MaxSpeedKmh,
            YearBuilt = request.YearBuilt,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim()
        };
        VesselSupport.ApplyRentalPrices(vessel, request.RentalPrices);

        try
        {
            _context.Vessels.Add(vessel);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (_databaseExceptionClassifier.IsUniqueConstraintViolation(ex))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã tàu hoặc số đăng ký tàu đã tồn tại.");
        }

        return VesselSupport.CreateDto(vessel);
    }
}
