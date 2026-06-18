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
            .MaximumLength(20)
            .WithMessage("Mã tàu không được vượt quá 20 ký tự.")
            .Matches("^[A-Za-z0-9_]+$")
            .WithMessage("Mã tàu chỉ được gồm chữ cái, số và dấu gạch dưới.");

        RuleFor(x => x.RegistrationNumber)
            .MaximumLength(50)
            .WithMessage("Số đăng ký tàu không được vượt quá 50 ký tự.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Tên tàu không được để trống.")
            .MaximumLength(100)
            .WithMessage("Tên tàu không được vượt quá 100 ký tự.");

        RuleFor(x => x.Status)
            .IsInEnum()
            .WithMessage("Trạng thái tàu không hợp lệ.");

        RuleFor(x => x.SeatCount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Số ghế không được âm.");

        RuleFor(x => x.NumberOfDecks)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Số tầng không được âm.");

        RuleFor(x => x.SeatSetupType)
            .IsInEnum()
            .WithMessage("Kiểu ghế của tàu không hợp lệ.");

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
            .Must(VesselSupport.IsValidImageUrl)
            .WithMessage("Đường dẫn ảnh không hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.ImageUrl));

        RuleForEach(x => x.ImageUrls)
            .MaximumLength(2048)
            .WithMessage("Đường dẫn ảnh không được vượt quá 2048 ký tự.")
            .Must(VesselSupport.IsValidImageUrl)
            .WithMessage("Đường dẫn ảnh không hợp lệ.");

        RuleFor(x => x)
            .Must(x => VesselSupport.HasValidRequestedImageCount(
                x.ImageUrl,
                x.ImageUrls,
                x.ImageContent,
                x.ImageFiles))
            .WithMessage($"Tàu chỉ được có tối đa {VesselSupport.MaxVesselImages} ảnh.");

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
                    .WithMessage("Giá thuê tàu phải lớn hơn 0.")
                    .LessThanOrEqualTo(9999999999.99m)
                    .WithMessage("Giá thuê tàu không hợp lệ.");

                price.RuleFor(x => x.Currency)
                    .Must(VesselSupport.IsValidCurrencyCode)
                    .WithMessage("Currency phải là mã ISO 4217 gồm 3 chữ cái, ví dụ VND.")
                    .When(x => x.Currency is not null);

                price.RuleFor(x => x.Note)
                    .MaximumLength(500)
                    .WithMessage("Ghi chú giá thuê tàu không được vượt quá 500 ký tự.");
            });
    }
}

public sealed class CreateVesselRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IDatabaseExceptionClassifier _databaseExceptionClassifier;
    private readonly IVesselImageStorageService _vesselImageStorage;

    public CreateVesselRequestUseCase(
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
        CreateVesselRequest request,
        CancellationToken cancellationToken)
    {
        await VesselSupport.EnsureCurrentUserCanManageVesselsAsync(_context, _userContext, cancellationToken);

        EnsureVesselCapacity(request.SeatCount, request.NumberOfDecks);
        EnsureInitialStatus(request.Status);

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

        var imageUrls = VesselSupport.NormalizeImageUrls(request.ImageUrl, request.ImageUrls);
        var imageFiles = VesselSupport.CreateImageFiles(
            request.ImageFileName,
            request.ImageContentType,
            request.ImageLength,
            request.ImageContent,
            request.ImageFiles);

        foreach (var imageFile in imageFiles)
        {
            VesselSupport.EnsureValidImage(
                "Image",
                imageFile.FileName,
                imageFile.ContentType,
                imageFile.Length,
                _vesselImageStorage);
        }

        var vessel = new Vessel
        {
            Code = normalizedCode,
            RegistrationNumber = normalizedRegistrationNumber,
            Name = request.Name.Trim(),
            Status = request.Status,
            SeatCount = request.SeatCount,
            NumberOfDecks = request.NumberOfDecks,
            SeatSetupType = request.SeatSetupType,
            MaxSpeedKmh = request.MaxSpeedKmh,
            YearBuilt = request.YearBuilt,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim()
        };

        foreach (var rentalPrice in request.RentalPrices ?? [])
        {
            vessel.RentalPrices.Add(VesselSupport.CreateRentalPrice(vessel.Id, rentalPrice));
        }

        var displayOrder = 1;
        foreach (var imageUrl in imageUrls)
        {
            vessel.Images.Add(VesselSupport.CreateImage(
                imageUrl,
                null,
                displayOrder++,
                isPrimary: vessel.Images.Count == 0));
        }
        VesselSupport.SyncPrimaryImage(vessel);

        try
        {
            _context.Vessels.Add(vessel);
            await _context.SaveChangesAsync(cancellationToken);

            foreach (var imageFile in imageFiles)
            {
                var vesselImage = new VesselImage
                {
                    VesselId = vessel.Id,
                    DisplayOrder = displayOrder++,
                    IsPrimary = vessel.Images.Count == 0
                };
                var uploadedImage = await _vesselImageStorage.UploadImageAsync(
                    new VesselImageUpload(
                        vessel.Id,
                        imageFile.Content,
                        imageFile.FileName,
                        imageFile.ContentType,
                        vesselImage.Id),
                    cancellationToken);

                vesselImage.Url = uploadedImage.Url;
                vesselImage.PublicId = uploadedImage.PublicId;
                vessel.Images.Add(vesselImage);
            }

            VesselSupport.SyncPrimaryImage(vessel);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (_databaseExceptionClassifier.IsUniqueConstraintViolation(ex))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã tàu hoặc số đăng ký tàu đã tồn tại.");
        }

        return VesselSupport.CreateDto(vessel);
    }

    private static void EnsureVesselCapacity(
        int seatCount,
        int numberOfDecks)
    {
        if (seatCount <= 0)
        {
            throw AuthSupport.CreateValidationException(nameof(CreateVesselRequest.SeatCount), "Tàu phải có số ghế lớn hơn 0 để setup sơ đồ ghế.");
        }

        if (numberOfDecks <= 0)
        {
            throw AuthSupport.CreateValidationException(nameof(CreateVesselRequest.NumberOfDecks), "Tàu phải có số tầng lớn hơn 0 để setup sơ đồ ghế.");
        }

    }

    private static void EnsureInitialStatus(VesselStatus status)
    {
        if (status == VesselStatus.Active)
        {
            throw AuthSupport.CreateValidationException(
                nameof(CreateVesselRequest.Status),
                "Tàu phải tạo ở trạng thái Inactive, setup đủ ghế rồi mới chuyển Active.");
        }
    }
}
