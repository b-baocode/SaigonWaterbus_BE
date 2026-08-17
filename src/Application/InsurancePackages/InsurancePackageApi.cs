using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.InsurancePackages;

public sealed record InsurancePackageDto(
    Guid InsurancePackageId,
    string Code,
    string Name,
    string BookingType,
    bool IsRequired,
    string? ProviderName,
    string? ProviderLogoUrl,
    string? ImageUrl,
    decimal UnitPremiumAmount,
    decimal CoverageAmount,
    string Currency,
    IReadOnlyList<string> Conditions,
    string? TermsUrl,
    InsurancePackageStatus Status,
    int DisplayOrder,
    int? RewardOption);

public sealed record GetInsurancePackageListQuery(
    string? BookingType = null,
    bool ActiveOnly = true) : IRequest<IReadOnlyList<InsurancePackageDto>>;

public sealed class GetInsurancePackageListQueryHandler
    : IRequestHandler<GetInsurancePackageListQuery, IReadOnlyList<InsurancePackageDto>>
{
    private readonly IApplicationDbContext _context;

    public GetInsurancePackageListQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<InsurancePackageDto>> Handle(
        GetInsurancePackageListQuery request,
        CancellationToken cancellationToken)
    {
        var query = _context.Set<InsurancePackage>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.BookingType))
        {
            var bookingType = InsurancePackageSupport.NormalizeBookingType(request.BookingType);
            var legacyBookingType = InsurancePackageSupport.NormalizeLegacyBookingType(request.BookingType);
            query = query.Where(x => x.BookingType == bookingType
                || legacyBookingType != null && x.BookingType == legacyBookingType);
        }

        if (request.ActiveOnly)
        {
            query = query.Where(x => x.IsActive);
        }

        return await query
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Code)
            .Select(x => InsurancePackageSupport.ToDto(x))
            .ToListAsync(cancellationToken);
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record CreateInsurancePackageCommand(
    string Code,
    string Name,
    string? BookingType,
    decimal UnitPremiumAmount,
    decimal CoverageAmount,
    bool IsRequired = false,
    string? ProviderName = null,
    string? ProviderLogoUrl = null,
    string? ImageUrl = null,
    IReadOnlyList<string>? Conditions = null,
    string? TermsUrl = null,
    InsurancePackageStatus Status = InsurancePackageStatus.Active,
    int? DisplayOrder = null,
    int? RewardOption = null) : IRequest<InsurancePackageDto>;

public sealed class CreateInsurancePackageCommandValidator : AbstractValidator<CreateInsurancePackageCommand>
{
    public CreateInsurancePackageCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(50)
            .Matches("^[A-Za-z][A-Za-z0-9_]*$")
            .WithMessage("Code chỉ gồm chữ, số và dấu gạch dưới, bắt đầu bằng chữ.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.BookingType)
            .Must(InsurancePackageSupport.IsKnownBookingType)
            .WithMessage("bookingType hợp lệ: PassengerInsurance. SeatBooking/CharterBooking chỉ giữ tương thích dữ liệu cũ.");
        RuleFor(x => x.UnitPremiumAmount)
            .GreaterThanOrEqualTo(1)
            .LessThanOrEqualTo(100_000_000)
            .Must(x => decimal.Truncate(x) == x)
            .WithMessage("unitPremiumAmount phải là số nguyên VND từ 0 đến 100.000.000.");
        RuleFor(x => x.CoverageAmount)
            .GreaterThan(0)
            .LessThanOrEqualTo(10_000_000_000)
            .Must(x => decimal.Truncate(x) == x)
            .WithMessage("coverageAmount phải là số nguyên VND lớn hơn 0.");
        RuleFor(x => x.ProviderName).MaximumLength(150).When(x => x.ProviderName is not null);
        RuleFor(x => x.ProviderLogoUrl)
            .MaximumLength(1000)
            .Must(InsurancePackageSupport.IsNullOrAbsoluteUrl)
            .WithMessage("providerLogoUrl phải là URL hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.ProviderLogoUrl));
        RuleFor(x => x.ImageUrl)
            .MaximumLength(1000)
            .Must(InsurancePackageSupport.IsNullOrAbsoluteUrl)
            .WithMessage("imageUrl phải là URL hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.ImageUrl));
        RuleFor(x => x.TermsUrl)
            .MaximumLength(1000)
            .Must(InsurancePackageSupport.IsNullOrAbsoluteUrl)
            .WithMessage("termsUrl phải là URL hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.TermsUrl));
        RuleFor(x => x.Conditions)
            .Must(InsurancePackageSupport.HaveValidConditions)
            .WithMessage("conditions tối đa 20 dòng, mỗi dòng tối đa 500 ký tự.");
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(1)
            .When(x => x.DisplayOrder.HasValue);
        RuleFor(x => x.RewardOption)
            .Must(x => x == 1 || x == 2 || x == null)
            .WithMessage("rewardOption: 1=dùng hết điểm thưởng, 2=không dùng, null=không chọn.");
    }
}

public sealed class CreateInsurancePackageCommandHandler
    : IRequestHandler<CreateInsurancePackageCommand, InsurancePackageDto>
{
    private readonly IApplicationDbContext _context;

    public CreateInsurancePackageCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<InsurancePackageDto> Handle(
        CreateInsurancePackageCommand request,
        CancellationToken cancellationToken)
    {
        var code = InsurancePackageSupport.NormalizeCode(request.Code);
        var bookingType = InsurancePackageSupport.NormalizeBookingType(request.BookingType);

        if (await _context.Set<InsurancePackage>()
                .AnyAsync(x => x.BookingType == bookingType && x.Code == code, cancellationToken))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Code),
                $"Gói bảo hiểm '{code}' cho {bookingType} đã tồn tại.")]);
        }

        var displayOrder = InsurancePackageSupport.ResolveDisplayOrder(
            _context, bookingType, request.DisplayOrder, cancellationToken);

        var package = new InsurancePackage
        {
            Code = code,
            Name = request.Name.Trim(),
            BookingType = bookingType,
            IsRequired = request.IsRequired,
            ProviderName = InsurancePackageSupport.TrimToNull(request.ProviderName),
            ProviderLogoUrl = InsurancePackageSupport.TrimToNull(request.ProviderLogoUrl),
            ImageUrl = InsurancePackageSupport.TrimToNull(request.ImageUrl),
            UnitPremiumAmount = request.UnitPremiumAmount,
            CoverageAmount = request.CoverageAmount,
            Currency = "VND",
            Conditions = InsurancePackageSupport.NormalizeConditions(request.Conditions),
            TermsUrl = InsurancePackageSupport.TrimToNull(request.TermsUrl),
            IsActive = InsurancePackageSupport.ToIsActive(request.Status),
            DisplayOrder = displayOrder,
            RewardOption = request.RewardOption
        };

        _context.Set<InsurancePackage>().Add(package);
        await _context.SaveChangesAsync(cancellationToken);

        return InsurancePackageSupport.ToDto(package);
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record UpdateInsurancePackageCommand(
    Guid InsurancePackageId,
    string Name,
    string? BookingType,
    decimal UnitPremiumAmount,
    decimal CoverageAmount,
    bool IsRequired,
    string? ProviderName,
    string? ProviderLogoUrl,
    string? ImageUrl,
    IReadOnlyList<string>? Conditions,
    string? TermsUrl,
    InsurancePackageStatus Status,
    int DisplayOrder,
    int? RewardOption) : IRequest<InsurancePackageDto>;

public sealed class UpdateInsurancePackageCommandValidator : AbstractValidator<UpdateInsurancePackageCommand>
{
    public UpdateInsurancePackageCommandValidator()
    {
        RuleFor(x => x.InsurancePackageId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.BookingType)
            .Must(InsurancePackageSupport.IsKnownBookingType)
            .WithMessage("bookingType hợp lệ: PassengerInsurance. SeatBooking/CharterBooking chỉ giữ tương thích dữ liệu cũ.");
        RuleFor(x => x.UnitPremiumAmount)
            .GreaterThanOrEqualTo(1)
            .LessThanOrEqualTo(100_000_000)
            .Must(x => decimal.Truncate(x) == x)
            .WithMessage("unitPremiumAmount phải là số nguyên VND từ 0 đến 100.000.000.");
        RuleFor(x => x.CoverageAmount)
            .GreaterThan(0)
            .LessThanOrEqualTo(10_000_000_000)
            .Must(x => decimal.Truncate(x) == x)
            .WithMessage("coverageAmount phải là số nguyên VND lớn hơn 0.");
        RuleFor(x => x.ProviderName).MaximumLength(150).When(x => x.ProviderName is not null);
        RuleFor(x => x.ProviderLogoUrl)
            .MaximumLength(1000)
            .Must(InsurancePackageSupport.IsNullOrAbsoluteUrl)
            .WithMessage("providerLogoUrl phải là URL hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.ProviderLogoUrl));
        RuleFor(x => x.ImageUrl)
            .MaximumLength(1000)
            .Must(InsurancePackageSupport.IsNullOrAbsoluteUrl)
            .WithMessage("imageUrl phải là URL hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.ImageUrl));
        RuleFor(x => x.TermsUrl)
            .MaximumLength(1000)
            .Must(InsurancePackageSupport.IsNullOrAbsoluteUrl)
            .WithMessage("termsUrl phải là URL hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.TermsUrl));
        RuleFor(x => x.Conditions)
            .Must(InsurancePackageSupport.HaveValidConditions)
            .WithMessage("conditions tối đa 20 dòng, mỗi dòng tối đa 500 ký tự.");
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.DisplayOrder)
            .Must(x => x == 1 || x == 2)
            .WithMessage("displayOrder: 1=dùng hết, 2=không dùng.");
        RuleFor(x => x.RewardOption)
            .Must(x => x == 1 || x == 2 || x == null)
            .WithMessage("rewardOption: 1=dùng hết điểm thưởng, 2=không dùng, null=không chọn.");
    }
}

public sealed class UpdateInsurancePackageCommandHandler
    : IRequestHandler<UpdateInsurancePackageCommand, InsurancePackageDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateInsurancePackageCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<InsurancePackageDto> Handle(
        UpdateInsurancePackageCommand request,
        CancellationToken cancellationToken)
    {
        var package = await _context.Set<InsurancePackage>()
            .SingleOrDefaultAsync(x => x.Id == request.InsurancePackageId, cancellationToken)
            ?? throw new NotFoundException("Insurance package not found.");
        var bookingType = InsurancePackageSupport.NormalizeBookingType(request.BookingType);

        if (!string.Equals(package.BookingType, bookingType, StringComparison.Ordinal)
            && await _context.Set<InsurancePackage>()
                .AnyAsync(x => x.Id != package.Id
                    && x.BookingType == bookingType
                    && x.Code == package.Code, cancellationToken))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.BookingType),
                $"Gói bảo hiểm '{package.Code}' cho {bookingType} đã tồn tại.")]);
        }

        package.Name = request.Name.Trim();
        package.BookingType = bookingType;
        package.IsRequired = request.IsRequired;
        package.ProviderName = InsurancePackageSupport.TrimToNull(request.ProviderName);
        package.ProviderLogoUrl = InsurancePackageSupport.TrimToNull(request.ProviderLogoUrl);
        package.ImageUrl = InsurancePackageSupport.TrimToNull(request.ImageUrl);
        package.UnitPremiumAmount = request.UnitPremiumAmount;
        package.CoverageAmount = request.CoverageAmount;
        package.Conditions = InsurancePackageSupport.NormalizeConditions(request.Conditions);
        package.TermsUrl = InsurancePackageSupport.TrimToNull(request.TermsUrl);
        package.IsActive = InsurancePackageSupport.ToIsActive(request.Status);
        package.RewardOption = request.RewardOption;

        InsurancePackageSupport.ShiftDisplayOrdersOnUpdate(
            _context, package.BookingType, package.Id, package.DisplayOrder, request.DisplayOrder, cancellationToken);
        package.DisplayOrder = request.DisplayOrder;

        await _context.SaveChangesAsync(cancellationToken);

        return InsurancePackageSupport.ToDto(package);
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record UpdateInsurancePackageStatusCommand(
    Guid InsurancePackageId,
    InsurancePackageStatus Status) : IRequest<InsurancePackageDto>;

public sealed class UpdateInsurancePackageStatusCommandValidator
    : AbstractValidator<UpdateInsurancePackageStatusCommand>
{
    public UpdateInsurancePackageStatusCommandValidator()
    {
        RuleFor(x => x.InsurancePackageId).NotEmpty();
        RuleFor(x => x.Status).IsInEnum();
    }
}

public sealed class UpdateInsurancePackageStatusCommandHandler
    : IRequestHandler<UpdateInsurancePackageStatusCommand, InsurancePackageDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateInsurancePackageStatusCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<InsurancePackageDto> Handle(
        UpdateInsurancePackageStatusCommand request,
        CancellationToken cancellationToken)
    {
        var package = await _context.Set<InsurancePackage>()
            .SingleOrDefaultAsync(x => x.Id == request.InsurancePackageId, cancellationToken)
            ?? throw new NotFoundException("Insurance package not found.");

        package.IsActive = InsurancePackageSupport.ToIsActive(request.Status);
        await _context.SaveChangesAsync(cancellationToken);

        return InsurancePackageSupport.ToDto(package);
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record UpdateInsurancePackageImageCommand(
    Guid InsurancePackageId,
    InsurancePackageImageFileRequest? ImageFile) : IRequest<InsurancePackageDto>;

public sealed record InsurancePackageImageFileRequest(
    string FileName,
    string ContentType,
    long FileSize,
    Stream Content);

public sealed class UpdateInsurancePackageImageCommandValidator
    : AbstractValidator<UpdateInsurancePackageImageCommand>
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB
    private static readonly string[] AllowedContentTypes =
    [
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp"
    ];

    public UpdateInsurancePackageImageCommandValidator()
    {
        RuleFor(x => x.InsurancePackageId).NotEmpty();
        RuleFor(x => x.ImageFile)
            .NotNull()
            .WithMessage("Vui lòng gửi file ảnh.");
        RuleFor(x => x.ImageFile!.ContentType)
            .Must(x => AllowedContentTypes.Contains(x, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Định dạng ảnh không hỗ trợ. Chỉ chấp nhận: {string.Join(", ", AllowedContentTypes)}.");
        RuleFor(x => x.ImageFile!.FileSize)
            .LessThanOrEqualTo(MaxFileSizeBytes)
            .WithMessage($"Dung lượng ảnh tối đa 5MB.");
    }
}

public sealed class UpdateInsurancePackageImageCommandHandler
    : IRequestHandler<UpdateInsurancePackageImageCommand, InsurancePackageDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateInsurancePackageImageCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<InsurancePackageDto> Handle(
        UpdateInsurancePackageImageCommand request,
        CancellationToken cancellationToken)
    {
        var package = await _context.Set<InsurancePackage>()
            .SingleOrDefaultAsync(x => x.Id == request.InsurancePackageId, cancellationToken)
            ?? throw new NotFoundException("Insurance package not found.");

        if (request.ImageFile is not null)
        {
            package.ImageUrl = await UploadImageAsync(request.ImageFile, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return InsurancePackageSupport.ToDto(package);
    }

    private async Task<string> UploadImageAsync(
        InsurancePackageImageFileRequest file,
        CancellationToken cancellationToken)
    {
        // TODO: Implement Azure Blob Storage upload or similar
        // For now, return a placeholder URL
        // In production, integrate with Azure Blob Storage / S3 / Cloudflare R2
        await using var memoryStream = new MemoryStream();
        await file.Content.CopyToAsync(memoryStream, cancellationToken);
        var content = memoryStream.ToArray();

        // Placeholder - replace with actual upload logic
        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var blobUrl = $"https://storage.example.com/insurance-packages/{fileName}";

        // TODO: Upload to blob storage and return the URL
        return blobUrl;
    }
}

internal static class InsurancePackageSupport
{
    public const string PassengerInsuranceBookingType = "PassengerInsurance";

    public static string NormalizeCode(string code) =>
        code.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    public static string NormalizeBookingType(string? bookingType)
    {
        if (string.IsNullOrWhiteSpace(bookingType)
            || string.Equals(bookingType, PassengerInsuranceBookingType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(bookingType, "Passenger", StringComparison.OrdinalIgnoreCase)
            || string.Equals(bookingType, "All", StringComparison.OrdinalIgnoreCase))
        {
            return PassengerInsuranceBookingType;
        }

        if (string.Equals(bookingType, Booking.SeatBookingType, StringComparison.OrdinalIgnoreCase))
        {
            return PassengerInsuranceBookingType;
        }

        if (string.Equals(bookingType, Booking.CharterBookingType, StringComparison.OrdinalIgnoreCase))
        {
            return PassengerInsuranceBookingType;
        }

        throw new ValidationException([new ValidationFailure(nameof(bookingType),
            "bookingType hợp lệ: PassengerInsurance. SeatBooking/CharterBooking chỉ giữ tương thích dữ liệu cũ.")]);
    }

    public static bool IsKnownBookingType(string? bookingType) =>
        string.IsNullOrWhiteSpace(bookingType)
        || string.Equals(bookingType, PassengerInsuranceBookingType, StringComparison.OrdinalIgnoreCase)
        || string.Equals(bookingType, "Passenger", StringComparison.OrdinalIgnoreCase)
        || string.Equals(bookingType, "All", StringComparison.OrdinalIgnoreCase)
        || string.Equals(bookingType, Booking.SeatBookingType, StringComparison.OrdinalIgnoreCase)
        || string.Equals(bookingType, Booking.CharterBookingType, StringComparison.OrdinalIgnoreCase);

    public static string? NormalizeLegacyBookingType(string? bookingType)
    {
        if (string.Equals(bookingType, Booking.SeatBookingType, StringComparison.OrdinalIgnoreCase))
        {
            return Booking.SeatBookingType;
        }

        if (string.Equals(bookingType, Booking.CharterBookingType, StringComparison.OrdinalIgnoreCase))
        {
            return Booking.CharterBookingType;
        }

        return null;
    }

    public static bool IsApplicableToBookingType(InsurancePackage package, string bookingType) =>
        string.Equals(package.BookingType, PassengerInsuranceBookingType, StringComparison.Ordinal)
        || string.Equals(package.BookingType, bookingType, StringComparison.Ordinal);

    public static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string[] NormalizeConditions(IReadOnlyList<string>? conditions) =>
        conditions?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToArray() ?? [];

    public static bool HaveValidConditions(IReadOnlyList<string>? conditions)
    {
        if (conditions is null)
        {
            return true;
        }

        var normalized = NormalizeConditions(conditions);
        return normalized.Length <= 20 && normalized.All(x => x.Length <= 500);
    }

    public static bool IsNullOrAbsoluteUrl(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static bool ToIsActive(InsurancePackageStatus status) =>
        status == InsurancePackageStatus.Active;

    public static InsurancePackageStatus ToStatus(bool isActive) =>
        isActive ? InsurancePackageStatus.Active : InsurancePackageStatus.Inactive;

    public static int ResolveDisplayOrder(
        IApplicationDbContext context,
        string bookingType,
        int? requestedOrder,
        CancellationToken cancellationToken = default)
    {
        if (requestedOrder.HasValue && requestedOrder.Value >= 1)
        {
            var maxOrder = context.Set<InsurancePackage>()
                .Where(x => x.BookingType == bookingType)
                .AsNoTracking()
                .Select(x => (int?)x.DisplayOrder)
                .Max();

            if (requestedOrder.Value <= (maxOrder ?? 0))
            {
                ShiftDisplayOrdersUp(context, bookingType, requestedOrder.Value, cancellationToken);
            }

            return requestedOrder.Value;
        }

        var nextMaxOrder = context.Set<InsurancePackage>()
            .Where(x => x.BookingType == bookingType)
            .AsNoTracking()
            .Select(x => (int?)x.DisplayOrder)
            .Max();

        return (nextMaxOrder ?? 0) + 1;
    }

    private static void ShiftDisplayOrdersUp(
        IApplicationDbContext context,
        string bookingType,
        int fromOrder,
        CancellationToken cancellationToken)
    {
        var packages = context.Set<InsurancePackage>()
            .Where(x => x.BookingType == bookingType && x.DisplayOrder >= fromOrder)
            .ToList();

        foreach (var pkg in packages)
        {
            pkg.DisplayOrder++;
        }
    }

    public static void ShiftDisplayOrdersOnUpdate(
        IApplicationDbContext context,
        string bookingType,
        Guid packageId,
        int oldOrder,
        int newOrder,
        CancellationToken cancellationToken)
    {
        if (oldOrder == newOrder) return;

        if (newOrder > oldOrder)
        {
            var packagesToShiftDown = context.Set<InsurancePackage>()
                .Where(x => x.BookingType == bookingType
                    && x.Id != packageId
                    && x.DisplayOrder > oldOrder
                    && x.DisplayOrder <= newOrder)
                .ToList();

            foreach (var pkg in packagesToShiftDown)
            {
                pkg.DisplayOrder--;
            }
        }
        else
        {
            var packagesToShiftUp = context.Set<InsurancePackage>()
                .Where(x => x.BookingType == bookingType
                    && x.Id != packageId
                    && x.DisplayOrder >= newOrder
                    && x.DisplayOrder < oldOrder)
                .ToList();

            foreach (var pkg in packagesToShiftUp)
            {
                pkg.DisplayOrder++;
            }
        }
    }

    public static InsurancePackageDto ToDto(InsurancePackage package) =>
        new(
            package.Id,
            package.Code,
            package.Name,
            package.BookingType,
            package.IsRequired,
            package.ProviderName,
            package.ProviderLogoUrl,
            package.ImageUrl,
            package.UnitPremiumAmount,
            package.CoverageAmount,
            package.Currency,
            package.Conditions,
            package.TermsUrl,
            ToStatus(package.IsActive),
            package.DisplayOrder,
            package.RewardOption);
}
