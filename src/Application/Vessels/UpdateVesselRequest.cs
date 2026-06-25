using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Vessels;

public sealed record UpdateVesselRequest(
    Guid VesselId,
    string? Code = null,
    string? Name = null,
    int? SeatCount = null,
    int? NumberOfDecks = null,
    string? RegistrationNumber = null,
    int? MaxSpeedKmh = null,
    int? YearBuilt = null,
    string? Description = null,
    string? ImageUrl = null,
    string? ImageFileName = null,
    string? ImageContentType = null,
    long? ImageLength = null,
    Stream? ImageContent = null,
    SeatSetupType? SeatSetupType = null,
    IReadOnlyCollection<string>? ImageUrls = null,
    IReadOnlyCollection<VesselImageFileRequest>? ImageFiles = null,
    IReadOnlyCollection<VesselRentalPriceRequest>? RentalPrices = null);

public sealed class UpdateVesselRequestValidator : AbstractValidator<UpdateVesselRequest>
{
    public UpdateVesselRequestValidator()
    {
        RuleFor(x => x.VesselId)
            .NotEmpty()
            .WithMessage("VesselId không hợp lệ.");

        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Mã tàu không được để trống.")
            .MaximumLength(50)
            .WithMessage("Mã tàu không được vượt quá 50 ký tự.")
            .Matches("^[A-Za-z0-9_]+$")
            .WithMessage("Mã tàu chỉ được gồm chữ cái, số và dấu gạch dưới.")
            .When(x => x.Code is not null);

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Tên tàu không được để trống.")
            .MaximumLength(150)
            .WithMessage("Tên tàu không được vượt quá 150 ký tự.")
            .When(x => x.Name is not null);

        RuleFor(x => x.SeatCount)
            .GreaterThan(0)
            .WithMessage("Số ghế phải lớn hơn 0.")
            .When(x => x.SeatCount.HasValue);

        RuleFor(x => x.NumberOfDecks)
            .GreaterThan(0)
            .WithMessage("Số tầng phải lớn hơn 0.")
            .When(x => x.NumberOfDecks.HasValue);

        RuleFor(x => x.SeatSetupType)
            .IsInEnum()
            .WithMessage("Kiểu ghế của tàu không hợp lệ.")
            .When(x => x.SeatSetupType.HasValue);

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .WithMessage("Mô tả tàu không được vượt quá 1000 ký tự.");

        RuleFor(x => x.ImageUrl)
            .MaximumLength(1000)
            .WithMessage("Đường dẫn ảnh không được vượt quá 1000 ký tự.")
            .Must(VesselSupport.IsValidImageUrl)
            .WithMessage("Đường dẫn ảnh không hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.ImageUrl));

        RuleFor(x => x.RentalPrices)
            .Must(VesselSupport.HasDistinctRentalUnits)
            .WithMessage("Mỗi đơn vị thuê tàu chỉ được cấu hình một giá.");
    }
}

public sealed class UpdateVesselRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IDatabaseExceptionClassifier _databaseExceptionClassifier;

    public UpdateVesselRequestUseCase(
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
        UpdateVesselRequest request,
        CancellationToken cancellationToken)
    {
        await VesselSupport.EnsureCurrentUserCanManageVesselsAsync(_context, _userContext, cancellationToken);

        if (request.ImageContent is not null || request.ImageFiles is { Count: > 0 })
        {
            throw AuthSupport.CreateValidationException(nameof(request.ImageContent), "Schema gọn chỉ hỗ trợ imageUrl, không lưu nhiều ảnh tàu.");
        }

        var vessel = await _context.Vessels
            .Include(x => x.Seats)
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        if (request.Code is not null)
        {
            var normalizedCode = VesselSupport.NormalizeCode(request.Code);
            if (!string.Equals(vessel.Code, normalizedCode, StringComparison.Ordinal)
                && await _context.Vessels.AnyAsync(x => x.Code == normalizedCode, cancellationToken))
            {
                throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã tàu đã tồn tại.");
            }

            vessel.Code = normalizedCode;
        }

        if (request.RegistrationNumber is not null)
        {
            var normalizedRegistrationNumber = VesselSupport.NormalizeRegistrationNumber(request.RegistrationNumber);
            if (normalizedRegistrationNumber is not null
                && !string.Equals(vessel.RegistrationNumber, normalizedRegistrationNumber, StringComparison.Ordinal)
                && await _context.Vessels.AnyAsync(x => x.RegistrationNumber == normalizedRegistrationNumber, cancellationToken))
            {
                throw AuthSupport.CreateValidationException(nameof(request.RegistrationNumber), "Số đăng ký tàu đã tồn tại.");
            }

            vessel.RegistrationNumber = normalizedRegistrationNumber;
        }

        if (request.Name is not null)
        {
            vessel.Name = request.Name.Trim();
        }

        var capacityChanged = (request.SeatCount.HasValue && request.SeatCount.Value != vessel.SeatCount)
            || (request.NumberOfDecks.HasValue && request.NumberOfDecks.Value != vessel.NumberOfDecks)
            || (request.SeatSetupType.HasValue && request.SeatSetupType.Value != vessel.SeatSetupType);
        if (capacityChanged && vessel.Seats.Count > 0)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.SeatCount),
                "Tàu đã có ghế. Xóa toàn bộ ghế trước khi đổi số ghế, số tầng hoặc kiểu ghế.");
        }

        if (request.SeatCount.HasValue)
        {
            vessel.SeatCount = request.SeatCount.Value;
        }

        if (request.NumberOfDecks.HasValue)
        {
            vessel.NumberOfDecks = request.NumberOfDecks.Value;
        }

        if (request.SeatSetupType.HasValue)
        {
            vessel.SeatSetupType = request.SeatSetupType.Value;
        }

        if (request.Description is not null)
        {
            vessel.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        }

        if (request.MaxSpeedKmh.HasValue)
        {
            vessel.MaxSpeedKmh = request.MaxSpeedKmh;
        }

        if (request.YearBuilt.HasValue)
        {
            vessel.YearBuilt = request.YearBuilt;
        }

        var imageUrl = VesselSupport.NormalizeImageUrls(request.ImageUrl, request.ImageUrls).FirstOrDefault();
        if (imageUrl is not null)
        {
            vessel.ImageUrl = imageUrl;
        }

        if (request.RentalPrices is not null)
        {
            VesselSupport.ApplyRentalPrices(vessel, request.RentalPrices);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (_databaseExceptionClassifier.IsUniqueConstraintViolation(ex))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã tàu hoặc số đăng ký tàu đã tồn tại.");
        }

        return VesselSupport.CreateDto(vessel);
    }
}
