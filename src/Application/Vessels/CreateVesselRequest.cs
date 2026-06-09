using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Vessels;

public sealed record CreateVesselRequest(
    Guid WaterbusServiceId,
    string Code,
    string Name,
    VesselStatus Status,
    int SeatCount,
    int PassengerCapacity,
    int NumberOfDecks,
    string? RegistrationNumber = null,
    int? MaxSpeedKmh = null,
    int? YearBuilt = null,
    string? Description = null,
    string? ImageUrl = null,
    string? ImageFileName = null,
    string? ImageContentType = null,
    long? ImageLength = null,
    Stream? ImageContent = null);

public sealed class CreateVesselRequestValidator : AbstractValidator<CreateVesselRequest>
{
    public CreateVesselRequestValidator()
    {
        RuleFor(x => x.WaterbusServiceId)
            .NotEmpty()
            .WithMessage("Dịch vụ WaterBus là bắt buộc.");

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

        RuleFor(x => x.PassengerCapacity)
            .GreaterThan(0)
            .WithMessage("Sức chứa hành khách phải lớn hơn 0.");

        RuleFor(x => x.NumberOfDecks)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Số tầng không được âm.");

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

        var service = await _context.WaterbusServices
            .SingleOrDefaultAsync(x => x.Id == request.WaterbusServiceId, cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(request.WaterbusServiceId), "Dịch vụ WaterBus không hợp lệ.");

        EnsureVesselCapacityMatchesService(service.BookingMode, request.SeatCount, request.PassengerCapacity, request.NumberOfDecks);
        EnsureInitialStatusMatchesService(service.BookingMode, request.Status);

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

        var vessel = new Vessel
        {
            WaterbusServiceId = service.Id,
            Code = normalizedCode,
            RegistrationNumber = normalizedRegistrationNumber,
            Name = request.Name.Trim(),
            Status = request.Status,
            SeatCount = request.SeatCount,
            PassengerCapacity = request.PassengerCapacity,
            NumberOfDecks = request.NumberOfDecks,
            MaxSpeedKmh = request.MaxSpeedKmh,
            YearBuilt = request.YearBuilt,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim()
        };

        if (hasImageUrl && !hasImageFile)
        {
            vessel.ImageUrl = request.ImageUrl!.Trim();
        }

        try
        {
            _context.Vessels.Add(vessel);
            await _context.SaveChangesAsync(cancellationToken);

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
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DbUpdateException ex) when (_databaseExceptionClassifier.IsUniqueConstraintViolation(ex))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã tàu hoặc số đăng ký tàu đã tồn tại.");
        }

        vessel.WaterbusService = service;
        return VesselSupport.CreateDto(vessel);
    }

    private static void EnsureVesselCapacityMatchesService(
        BookingMode bookingMode,
        int seatCount,
        int passengerCapacity,
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

        if (passengerCapacity < seatCount)
        {
            throw AuthSupport.CreateValidationException(nameof(CreateVesselRequest.PassengerCapacity), "Sức chứa hành khách phải lớn hơn hoặc bằng số ghế.");
        }
    }

    private static void EnsureInitialStatusMatchesService(BookingMode bookingMode, VesselStatus status)
    {
        if (status == VesselStatus.Active)
        {
            throw AuthSupport.CreateValidationException(
                nameof(CreateVesselRequest.Status),
                "Tàu phải tạo ở trạng thái Inactive, setup đủ ghế rồi mới chuyển Active.");
        }
    }
}
