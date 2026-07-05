using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Boats;

public sealed record UpdateBoatRequest(
    Guid BoatId,
    string? Code = null,
    string? Name = null,
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
    IReadOnlyCollection<BoatImageFileRequest>? ImageFiles = null,
    IReadOnlyCollection<BoatRentalPriceRequest>? RentalPrices = null);

public sealed class UpdateBoatRequestValidator : AbstractValidator<UpdateBoatRequest>
{
    public UpdateBoatRequestValidator()
    {
        RuleFor(x => x.BoatId)
            .NotEmpty()
            .WithMessage("BoatId không hợp lệ.");

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
            .WithMessage("Schema gọn chỉ lưu 1 ảnh chính cho mỗi tàu.");

        RuleFor(x => x.RentalPrices)
            .Must(BoatSupport.HasDistinctRentalUnits)
            .WithMessage("Mỗi đơn vị thuê tàu chỉ được cấu hình một giá.");
    }
}

public sealed class UpdateBoatRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IDatabaseExceptionClassifier _databaseExceptionClassifier;
    private readonly IBoatImageStorageService? _boatImageStorageService;

    public UpdateBoatRequestUseCase(
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
        UpdateBoatRequest request,
        CancellationToken cancellationToken)
    {
        await BoatSupport.EnsureCurrentUserCanManageBoatsAsync(_context, _userContext, cancellationToken);

        var boat = await _context.Boats
            .Include(x => x.Seats)
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        if (request.Code is not null)
        {
            var normalizedCode = BoatSupport.NormalizeCode(request.Code);
            if (!string.Equals(boat.Code, normalizedCode, StringComparison.Ordinal)
                && await _context.Boats.AnyAsync(x => x.Code == normalizedCode, cancellationToken))
            {
                throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã tàu đã tồn tại.");
            }

            boat.Code = normalizedCode;
        }

        if (request.RegistrationNumber is not null)
        {
            var normalizedRegistrationNumber = BoatSupport.NormalizeRegistrationNumber(request.RegistrationNumber);
            if (normalizedRegistrationNumber is not null
                && !string.Equals(boat.RegistrationNumber, normalizedRegistrationNumber, StringComparison.Ordinal)
                && await _context.Boats.AnyAsync(x => x.RegistrationNumber == normalizedRegistrationNumber, cancellationToken))
            {
                throw AuthSupport.CreateValidationException(nameof(request.RegistrationNumber), "Số đăng ký tàu đã tồn tại.");
            }

            boat.RegistrationNumber = normalizedRegistrationNumber;
        }

        if (request.Name is not null)
        {
            boat.Name = request.Name.Trim();
        }

        var capacityChanged = (request.NumberOfDecks.HasValue && request.NumberOfDecks.Value != boat.NumberOfDecks)
            || (request.SeatSetupType.HasValue && request.SeatSetupType.Value != boat.SeatSetupType);
        if (capacityChanged && boat.Seats.Count > 0)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.NumberOfDecks),
                "Tàu đã có ghế. Xóa toàn bộ ghế trước khi đổi số tầng hoặc kiểu ghế.");
        }

        if (request.NumberOfDecks.HasValue)
        {
            boat.NumberOfDecks = request.NumberOfDecks.Value;
        }

        if (request.SeatSetupType.HasValue)
        {
            boat.SeatSetupType = request.SeatSetupType.Value;
        }

        if (request.Description is not null)
        {
            boat.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        }

        if (request.MaxSpeedKmh.HasValue)
        {
            boat.MaxSpeedKmh = request.MaxSpeedKmh;
        }

        if (request.YearBuilt.HasValue)
        {
            boat.YearBuilt = request.YearBuilt;
        }

        var imageUrl = BoatSupport.NormalizeImageUrls(request.ImageUrl, request.ImageUrls).FirstOrDefault();
        if (imageUrl is not null)
        {
            boat.ImageUrl = imageUrl;
            boat.ImagePublicId = null;
        }

        var uploadedImage = await UploadPrimaryImageAsync(boat.Id, request, cancellationToken);
        if (uploadedImage is not null)
        {
            boat.ImageUrl = uploadedImage.Url;
            boat.ImagePublicId = uploadedImage.PublicId;
        }

        if (request.RentalPrices is not null)
        {
            BoatSupport.ApplyRentalPrices(boat, request.RentalPrices);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (_databaseExceptionClassifier.IsUniqueConstraintViolation(ex))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã tàu hoặc số đăng ký tàu đã tồn tại.");
        }

        return BoatSupport.CreateDto(boat);
    }

    private async Task<StoredBoatImage?> UploadPrimaryImageAsync(
        Guid boatId,
        UpdateBoatRequest request,
        CancellationToken cancellationToken)
    {
        var files = BoatSupport.CreateImageFiles(
                request.ImageFileName,
                request.ImageContentType,
                request.ImageLength,
                request.ImageContent,
                request.ImageFiles)
            .ToArray();

        if (files.Length == 0)
        {
            return null;
        }

        var boatImageStorage = _boatImageStorageService
            ?? throw AuthSupport.CreateValidationException(nameof(request.ImageContent), "Dịch vụ lưu ảnh tàu chưa được cấu hình.");

        var file = files[0];
        BoatSupport.EnsureValidImage(
            nameof(request.ImageContent),
            file.FileName,
            file.ContentType,
            file.Length,
            boatImageStorage);

        if (file.Content.CanSeek)
        {
            file.Content.Position = 0;
        }

        return await boatImageStorage.UploadImageAsync(
            new BoatImageUpload(boatId, file.Content, file.FileName, file.ContentType),
            cancellationToken);
    }
}
