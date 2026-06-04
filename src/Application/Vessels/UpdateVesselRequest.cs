using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Vessels;

public sealed record UpdateVesselRequest(
    int VesselId,
    int? WaterbusServiceId = null,
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
    Stream? ImageContent = null);

public sealed class UpdateVesselRequestValidator : AbstractValidator<UpdateVesselRequest>
{
    public UpdateVesselRequestValidator()
    {
        RuleFor(x => x.VesselId)
            .GreaterThan(0)
            .WithMessage("VesselId không hợp lệ.");

        RuleFor(x => x.WaterbusServiceId)
            .GreaterThan(0)
            .WithMessage("Dịch vụ WaterBus không hợp lệ.")
            .When(x => x.WaterbusServiceId.HasValue);

        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Mã tàu không được để trống.")
            .MaximumLength(20)
            .WithMessage("Mã tàu không được vượt quá 20 ký tự.")
            .Matches("^[A-Za-z0-9_]+$")
            .WithMessage("Mã tàu chỉ được gồm chữ cái, số và dấu gạch dưới.")
            .When(x => x.Code is not null);

        RuleFor(x => x.RegistrationNumber)
            .MaximumLength(50)
            .WithMessage("Số đăng ký tàu không được vượt quá 50 ký tự.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Tên tàu không được để trống.")
            .MaximumLength(100)
            .WithMessage("Tên tàu không được vượt quá 100 ký tự.")
            .When(x => x.Name is not null);

        RuleFor(x => x.SeatCount)
            .GreaterThan(0)
            .WithMessage("Số ghế phải lớn hơn 0.")
            .When(x => x.SeatCount.HasValue);

        RuleFor(x => x.NumberOfDecks)
            .GreaterThan(0)
            .WithMessage("Số tầng phải lớn hơn 0.")
            .When(x => x.NumberOfDecks.HasValue);

        RuleFor(x => x.MaxSpeedKmh)
            .GreaterThan(0)
            .WithMessage("Tốc độ tối đa phải lớn hơn 0.")
            .When(x => x.MaxSpeedKmh.HasValue);

        RuleFor(x => x.YearBuilt)
            .InclusiveBetween(1900, DateTime.UtcNow.Year)
            .WithMessage("Năm đóng tàu không hợp lệ.")
            .When(x => x.YearBuilt.HasValue);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .WithMessage("Mô tả tàu không được vượt quá 500 ký tự.");

        RuleFor(x => x.ImageUrl)
            .MaximumLength(2048)
            .WithMessage("Đường dẫn ảnh không được vượt quá 2048 ký tự.")
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("Đường dẫn ảnh không hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.ImageUrl));
    }
}

public sealed class UpdateVesselRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IDatabaseExceptionClassifier _databaseExceptionClassifier;
    private readonly IVesselImageStorageService _vesselImageStorage;

    public UpdateVesselRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext,
        IDatabaseExceptionClassifier databaseExceptionClassifier,
        IVesselImageStorageService vesselImageStorage)
    {
        _context = context;
        _userContext = userContext;
        _databaseExceptionClassifier = databaseExceptionClassifier;
        _vesselImageStorage = vesselImageStorage;
    }

    public async Task<VesselDto> ExecuteAsync(
        UpdateVesselRequest request,
        CancellationToken cancellationToken)
    {
        await VesselSupport.EnsureCurrentUserCanManageVesselsAsync(_context, _userContext, cancellationToken);

        var vessel = await _context.Vessels
            .Include(x => x.WaterbusService)
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        if (request.WaterbusServiceId.HasValue && request.WaterbusServiceId.Value != vessel.WaterbusServiceId)
        {
            var service = await _context.WaterbusServices
                .SingleOrDefaultAsync(x => x.Id == request.WaterbusServiceId.Value, cancellationToken)
                ?? throw AuthSupport.CreateValidationException(nameof(request.WaterbusServiceId), "Dịch vụ WaterBus không hợp lệ.");

            vessel.WaterbusServiceId = service.Id;
            vessel.WaterbusService = service;
        }

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

        if (request.SeatCount.HasValue)
        {
            vessel.SeatCount = request.SeatCount.Value;
        }

        if (request.NumberOfDecks.HasValue)
        {
            vessel.NumberOfDecks = request.NumberOfDecks.Value;
        }

        if (request.MaxSpeedKmh.HasValue)
        {
            vessel.MaxSpeedKmh = request.MaxSpeedKmh.Value;
        }

        if (request.YearBuilt.HasValue)
        {
            vessel.YearBuilt = request.YearBuilt.Value;
        }

        if (request.Description is not null)
        {
            vessel.Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim();
        }

        var hasImageFile = request.ImageContent is not null;
        var hasImageUrl = !string.IsNullOrWhiteSpace(request.ImageUrl);

        if (hasImageFile)
        {
            VesselSupport.EnsureValidImage(
                "Image",
                request.ImageFileName,
                request.ImageContentType,
                request.ImageLength,
                _vesselImageStorage);
        }

        if (hasImageUrl && !hasImageFile)
        {
            vessel.ImageUrl = request.ImageUrl!.Trim();
            vessel.ImagePublicId = null;
        }

        try
        {
            if (hasImageFile)
            {
                var uploadedImage = await _vesselImageStorage.UploadImageAsync(
                    new VesselImageUpload(
                        vessel.Id,
                        request.ImageContent!,
                        request.ImageFileName!,
                        request.ImageContentType),
                    cancellationToken);

                vessel.ImageUrl = uploadedImage.Url;
                vessel.ImagePublicId = uploadedImage.PublicId;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (_databaseExceptionClassifier.IsUniqueConstraintViolation(ex))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã tàu hoặc số đăng ký tàu đã tồn tại.");
        }

        return VesselSupport.CreateDto(vessel);
    }
}
