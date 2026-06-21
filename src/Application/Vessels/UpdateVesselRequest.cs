using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
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
            .GreaterThanOrEqualTo(0)
            .WithMessage("Số ghế không được âm.")
            .When(x => x.SeatCount.HasValue);

        RuleFor(x => x.NumberOfDecks)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Số tầng không được âm.")
            .When(x => x.NumberOfDecks.HasValue);

        RuleFor(x => x.SeatSetupType)
            .IsInEnum()
            .WithMessage("Kiểu ghế của tàu không hợp lệ.")
            .When(x => x.SeatSetupType.HasValue);

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
            .Include(x => x.RentalPrices)
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

        var seatCountChanged = request.SeatCount.HasValue && request.SeatCount.Value != vessel.SeatCount;
        var numberOfDecksChanged = request.NumberOfDecks.HasValue && request.NumberOfDecks.Value != vessel.NumberOfDecks;
        var seatSetupTypeChanged = request.SeatSetupType.HasValue
            && request.SeatSetupType.Value != vessel.SeatSetupType;
        if (seatCountChanged || numberOfDecksChanged || seatSetupTypeChanged)
        {
            var hasSeatLayout = vessel.SeatsConfigured
                || await _context.Seats.AnyAsync(x => x.VesselId == vessel.Id, cancellationToken)
                || await _context.VesselDeckLayouts.AnyAsync(x => x.VesselId == vessel.Id, cancellationToken)
                || await _context.VesselFacilities.AnyAsync(x => x.VesselId == vessel.Id, cancellationToken)
                || await _context.VesselLayoutCells.AnyAsync(x => x.VesselId == vessel.Id, cancellationToken);

            if (hasSeatLayout)
            {
                throw AuthSupport.CreateValidationException(
                    nameof(request.SeatCount),
                    "Tàu đã setup sơ đồ ghế. Xóa toàn bộ sơ đồ ghế trước khi đổi số ghế, số tầng hoặc kiểu ghế.");
            }
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

        EnsureVesselCapacity(vessel.SeatCount, vessel.NumberOfDecks);

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

        if (request.RentalPrices is not null)
        {
            UpsertRentalPrices(vessel, request.RentalPrices);
        }

        var imageUrls = VesselSupport.NormalizeImageUrls(request.ImageUrl, request.ImageUrls);
        var imageFiles = VesselSupport.CreateImageFiles(
            request.ImageFileName,
            request.ImageContentType,
            request.ImageLength,
            request.ImageContent,
            request.ImageFiles);
        var hasImageUpdate = imageUrls.Count > 0 || imageFiles.Count > 0;

        foreach (var imageFile in imageFiles)
        {
            VesselSupport.EnsureValidImage(
                "Image",
                imageFile.FileName,
                imageFile.ContentType,
                imageFile.Length,
                _vesselImageStorage);
        }

        try
        {
            if (hasImageUpdate)
            {
                if (SupportsTransactionalBulkImageReplace())
                {
                    await using var transaction = await _context.BeginTransactionAsync(cancellationToken);
                    await ReplaceImagesAsync(vessel, imageUrls, imageFiles, useBulkDelete: true, cancellationToken);
                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                else
                {
                    await ReplaceImagesAsync(vessel, imageUrls, imageFiles, useBulkDelete: false, cancellationToken);
                    await _context.SaveChangesAsync(cancellationToken);
                }
            }
            else
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DbUpdateException ex) when (_databaseExceptionClassifier.IsUniqueConstraintViolation(ex))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã tàu hoặc số đăng ký tàu đã tồn tại.");
        }

        await LoadImagesAsync(vessel, cancellationToken);
        return VesselSupport.CreateDto(vessel);
    }

    private async Task ReplaceImagesAsync(
        Vessel vessel,
        IReadOnlyCollection<string> imageUrls,
        IReadOnlyCollection<VesselImageFileRequest> imageFiles,
        bool useBulkDelete,
        CancellationToken cancellationToken)
    {
        if (useBulkDelete)
        {
            await _context.VesselImages
                .Where(x => x.VesselId == vessel.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            var existingImages = await _context.VesselImages
                .Where(x => x.VesselId == vessel.Id)
                .ToListAsync(cancellationToken);
            _context.VesselImages.RemoveRange(existingImages);
        }

        vessel.Images.Clear();
        vessel.ImageUrl = null;
        vessel.ImagePublicId = null;

        var displayOrder = 1;
        foreach (var imageUrl in imageUrls)
        {
            AddImage(
                vessel,
                VesselSupport.CreateImage(
                    imageUrl,
                    null,
                    displayOrder++,
                    isPrimary: vessel.Images.Count == 0));
        }

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
            AddImage(vessel, vesselImage);
        }

        VesselSupport.SyncPrimaryImage(vessel);
    }

    private void AddImage(Vessel vessel, VesselImage image)
    {
        image.VesselId = vessel.Id;
        _context.VesselImages.Add(image);
        vessel.Images.Add(image);
    }

    private async Task LoadImagesAsync(Vessel vessel, CancellationToken cancellationToken)
    {
        vessel.Images = await _context.VesselImages
            .Where(x => x.VesselId == vessel.Id)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    private bool SupportsTransactionalBulkImageReplace() =>
        _context is DbContext dbContext
        && !string.Equals(
            dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.InMemory",
            StringComparison.Ordinal);

    private void UpsertRentalPrices(
        Vessel vessel,
        IReadOnlyCollection<VesselRentalPriceRequest> rentalPrices)
    {
        foreach (var request in rentalPrices)
        {
            var rentalPrice = vessel.RentalPrices.SingleOrDefault(x => x.RentalUnit == request.RentalUnit);
            if (rentalPrice is null)
            {
                rentalPrice = new VesselRentalPrice
                {
                    VesselId = vessel.Id,
                    RentalUnit = request.RentalUnit
                };
                vessel.RentalPrices.Add(rentalPrice);
                _context.VesselRentalPrices.Add(rentalPrice);
            }

            rentalPrice.UnitPrice = request.UnitPrice;
            rentalPrice.Currency = VesselSupport.NormalizeCurrency(request.Currency);
            rentalPrice.Note = VesselSupport.NormalizeOptionalNote(request.Note);
        }
    }

    private static void EnsureVesselCapacity(
        int seatCount,
        int numberOfDecks)
    {
        if (seatCount <= 0)
        {
            throw AuthSupport.CreateValidationException(nameof(UpdateVesselRequest.SeatCount), "Tàu phải có số ghế lớn hơn 0 để setup sơ đồ ghế.");
        }

        if (numberOfDecks <= 0)
        {
            throw AuthSupport.CreateValidationException(nameof(UpdateVesselRequest.NumberOfDecks), "Tàu phải có số tầng lớn hơn 0 để setup sơ đồ ghế.");
        }

    }
}
