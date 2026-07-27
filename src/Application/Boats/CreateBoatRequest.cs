using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Boats;

public sealed record CreateBoatRequest(
    string Code,
    string Name,
    BoatStatus Status,
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
    BoatServiceType ServiceType = BoatServiceType.Passenger,
    SeatSetupType SeatSetupType = SeatSetupType.FullStandard,
    IReadOnlyCollection<string>? ImageUrls = null,
    IReadOnlyCollection<BoatImageFileRequest>? ImageFiles = null);

public sealed class CreateBoatRequestValidator : AbstractValidator<CreateBoatRequest>
{
    public CreateBoatRequestValidator()
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

        RuleFor(x => x.ServiceType)
            .IsInEnum()
            .WithMessage("Loại tàu không hợp lệ.");

        RuleFor(x => x.SeatCount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Số ghế không được âm.");

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
            .Must(BoatSupport.IsValidImageUrl)
            .WithMessage("Đường dẫn ảnh không hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.ImageUrl));

        RuleForEach(x => x.ImageUrls)
            .MaximumLength(1000)
            .WithMessage("Đường dẫn ảnh không được vượt quá 1000 ký tự.")
            .Must(BoatSupport.IsValidImageUrl)
            .WithMessage("Đường dẫn ảnh không hợp lệ.");

        RuleFor(x => x)
            .Must(x => BoatSupport.HasValidRequestedImageCount(
                x.ImageUrl,
                x.ImageUrls,
                x.ImageContent,
                x.ImageFiles))
            .WithMessage("Mỗi tàu chỉ được gửi tối đa 3 ảnh.");

    }
}

public sealed class CreateBoatRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IDatabaseExceptionClassifier _databaseExceptionClassifier;
    private readonly IBoatImageStorageService? _boatImageStorageService;

    public CreateBoatRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext,
        IDatabaseExceptionClassifier databaseExceptionClassifier,
        IBoatImageStorageService? boatImageStorageService = null)
    {
        _context = context;
        _userContext = userContext;
        _databaseExceptionClassifier = databaseExceptionClassifier;
        _boatImageStorageService = boatImageStorageService;
    }

    public async Task<BoatDto> ExecuteAsync(
        CreateBoatRequest request,
        CancellationToken cancellationToken)
    {
        await BoatSupport.EnsureCurrentUserCanManageBoatsAsync(_context, _userContext, cancellationToken);

        if (request.Status == BoatStatus.Active)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.Status),
                "Tạo tàu mới cần để Inactive, cấu hình đủ ghế xong mới chuyển Active.");
        }

        var normalizedCode = BoatSupport.NormalizeCode(request.Code);
        if (await _context.Boats.AnyAsync(x => x.Code == normalizedCode, cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã tàu đã tồn tại.");
        }

        var normalizedRegistrationNumber = BoatSupport.NormalizeRegistrationNumber(request.RegistrationNumber);
        if (normalizedRegistrationNumber is not null
            && await _context.Boats.AnyAsync(x => x.RegistrationNumber == normalizedRegistrationNumber, cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(request.RegistrationNumber), "Số đăng ký tàu đã tồn tại.");
        }

        var imageUrls = BoatSupport.NormalizeImageUrls(request.ImageUrl, request.ImageUrls).ToList();
        var boat = new Boat
        {
            Code = normalizedCode,
            RegistrationNumber = normalizedRegistrationNumber,
            Name = request.Name.Trim(),
            ServiceType = request.ServiceType,
            Status = request.Status,
            SeatCount = 0,
            NumberOfDecks = request.NumberOfDecks,
            SeatSetupType = request.SeatSetupType,
            MaxSpeedKmh = request.MaxSpeedKmh,
            YearBuilt = request.YearBuilt,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim()
        };

        var imageFiles = BoatSupport.CreateImageFiles(
            request.ImageFileName,
            request.ImageContentType,
            request.ImageLength,
            request.ImageContent,
            request.ImageFiles);
        var uploadedImages = await BoatSupport.UploadImagesAsync(
            boat.Id,
            imageFiles,
            _boatImageStorageService,
            nameof(request.ImageFiles),
            cancellationToken);
        imageUrls.AddRange(uploadedImages.Select(image => image.Url));
        BoatSupport.ReplaceImages(boat, imageUrls, uploadedImages);

        try
        {
            _context.Boats.Add(boat);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (_databaseExceptionClassifier.IsUniqueConstraintViolation(ex))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã tàu hoặc số đăng ký tàu đã tồn tại.");
        }

        return BoatSupport.CreateDto(boat);
    }
}
