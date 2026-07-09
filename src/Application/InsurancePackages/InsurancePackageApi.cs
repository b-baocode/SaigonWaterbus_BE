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
    decimal UnitPremiumAmount,
    decimal CoverageAmount,
    string Currency,
    IReadOnlyList<string> Conditions,
    string? TermsUrl,
    InsurancePackageStatus Status,
    int DisplayOrder);

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
            query = query.Where(x => x.BookingType == bookingType);
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
    string BookingType,
    decimal UnitPremiumAmount,
    decimal CoverageAmount,
    bool IsRequired = false,
    string? ProviderName = null,
    string? ProviderLogoUrl = null,
    IReadOnlyList<string>? Conditions = null,
    string? TermsUrl = null,
    InsurancePackageStatus Status = InsurancePackageStatus.Active,
    int? DisplayOrder = null) : IRequest<InsurancePackageDto>;

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
            .NotEmpty()
            .Must(InsurancePackageSupport.IsKnownBookingType)
            .WithMessage("bookingType hợp lệ: SeatBooking | CharterBooking.");
        RuleFor(x => x.UnitPremiumAmount)
            .GreaterThanOrEqualTo(0)
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
        RuleFor(x => x.TermsUrl)
            .MaximumLength(1000)
            .Must(InsurancePackageSupport.IsNullOrAbsoluteUrl)
            .WithMessage("termsUrl phải là URL hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.TermsUrl));
        RuleFor(x => x.Conditions)
            .Must(InsurancePackageSupport.HaveValidConditions)
            .WithMessage("conditions tối đa 20 dòng, mỗi dòng tối đa 500 ký tự.");
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.DisplayOrder).GreaterThan(0).When(x => x.DisplayOrder.HasValue);
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

        var displayOrder = request.DisplayOrder
            ?? (await _context.Set<InsurancePackage>()
                .Where(x => x.BookingType == bookingType)
                .MaxAsync(x => (int?)x.DisplayOrder, cancellationToken) ?? 0) + 1;

        var package = new InsurancePackage
        {
            Code = code,
            Name = request.Name.Trim(),
            BookingType = bookingType,
            IsRequired = request.IsRequired,
            ProviderName = InsurancePackageSupport.TrimToNull(request.ProviderName),
            ProviderLogoUrl = InsurancePackageSupport.TrimToNull(request.ProviderLogoUrl),
            UnitPremiumAmount = request.UnitPremiumAmount,
            CoverageAmount = request.CoverageAmount,
            Currency = "VND",
            Conditions = InsurancePackageSupport.NormalizeConditions(request.Conditions),
            TermsUrl = InsurancePackageSupport.TrimToNull(request.TermsUrl),
            IsActive = InsurancePackageSupport.ToIsActive(request.Status),
            DisplayOrder = displayOrder
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
    string BookingType,
    decimal UnitPremiumAmount,
    decimal CoverageAmount,
    bool IsRequired,
    string? ProviderName,
    string? ProviderLogoUrl,
    IReadOnlyList<string>? Conditions,
    string? TermsUrl,
    InsurancePackageStatus Status,
    int DisplayOrder) : IRequest<InsurancePackageDto>;

public sealed class UpdateInsurancePackageCommandValidator : AbstractValidator<UpdateInsurancePackageCommand>
{
    public UpdateInsurancePackageCommandValidator()
    {
        RuleFor(x => x.InsurancePackageId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.BookingType)
            .NotEmpty()
            .Must(InsurancePackageSupport.IsKnownBookingType)
            .WithMessage("bookingType hợp lệ: SeatBooking | CharterBooking.");
        RuleFor(x => x.UnitPremiumAmount)
            .GreaterThanOrEqualTo(0)
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
        RuleFor(x => x.TermsUrl)
            .MaximumLength(1000)
            .Must(InsurancePackageSupport.IsNullOrAbsoluteUrl)
            .WithMessage("termsUrl phải là URL hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.TermsUrl));
        RuleFor(x => x.Conditions)
            .Must(InsurancePackageSupport.HaveValidConditions)
            .WithMessage("conditions tối đa 20 dòng, mỗi dòng tối đa 500 ký tự.");
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.DisplayOrder).GreaterThan(0);
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
        package.UnitPremiumAmount = request.UnitPremiumAmount;
        package.CoverageAmount = request.CoverageAmount;
        package.Conditions = InsurancePackageSupport.NormalizeConditions(request.Conditions);
        package.TermsUrl = InsurancePackageSupport.TrimToNull(request.TermsUrl);
        package.IsActive = InsurancePackageSupport.ToIsActive(request.Status);
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

internal static class InsurancePackageSupport
{
    public static string NormalizeCode(string code) =>
        code.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    public static string NormalizeBookingType(string bookingType)
    {
        if (string.Equals(bookingType, Booking.SeatBookingType, StringComparison.OrdinalIgnoreCase))
        {
            return Booking.SeatBookingType;
        }

        if (string.Equals(bookingType, Booking.CharterBookingType, StringComparison.OrdinalIgnoreCase))
        {
            return Booking.CharterBookingType;
        }

        throw new ValidationException([new ValidationFailure(nameof(bookingType),
            "bookingType hợp lệ: SeatBooking | CharterBooking.")]);
    }

    public static bool IsKnownBookingType(string? bookingType) =>
        string.Equals(bookingType, Booking.SeatBookingType, StringComparison.OrdinalIgnoreCase)
        || string.Equals(bookingType, Booking.CharterBookingType, StringComparison.OrdinalIgnoreCase);

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

    public static InsurancePackageDto ToDto(InsurancePackage package) =>
        new(
            package.Id,
            package.Code,
            package.Name,
            package.BookingType,
            package.IsRequired,
            package.ProviderName,
            package.ProviderLogoUrl,
            package.UnitPremiumAmount,
            package.CoverageAmount,
            package.Currency,
            package.Conditions,
            package.TermsUrl,
            ToStatus(package.IsActive),
            package.DisplayOrder);
}
