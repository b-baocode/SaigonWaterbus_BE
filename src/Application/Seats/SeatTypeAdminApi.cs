using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Seats;

public sealed record SeatTypeAdminDto(
    Guid? SeatTypeId,
    string Code,
    string Name,
    decimal BasePrice,
    string Currency,
    bool IsActive,
    int DisplayOrder);

/// <summary>Giá gốc theo loại ghế: đọc từ bảng seat_types, thiếu dòng nào thì fallback bảng cứng SeatTypePricing.</summary>
internal static class SeatTypeBasePriceSupport
{
    public static readonly string[] KnownCodes = ["STANDARD", "CABIN", "RIVER", "SKY"];

    public static async Task<IReadOnlyDictionary<string, decimal>> GetBasePricesAsync(
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in KnownCodes)
        {
            prices[code] = SeatTypePricing.GetBasePrice(code);
        }

        var rows = await context.Set<SeatType>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            if (row.BasePrice > 0)
            {
                prices[row.Code] = row.BasePrice;
            }
        }

        return prices;
    }
}

public sealed record GetSeatTypeListQuery : IRequest<IReadOnlyList<SeatTypeAdminDto>>;

public sealed class GetSeatTypeListQueryHandler
    : IRequestHandler<GetSeatTypeListQuery, IReadOnlyList<SeatTypeAdminDto>>
{
    private readonly IApplicationDbContext _context;

    public GetSeatTypeListQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<SeatTypeAdminDto>> Handle(
        GetSeatTypeListQuery request,
        CancellationToken cancellationToken)
    {
        var rows = await _context.Set<SeatType>()
            .AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Code)
            .ToListAsync(cancellationToken);

        if (rows.Count > 0)
        {
            return rows
                .Select(x => new SeatTypeAdminDto(
                    x.Id, x.Code, x.Name, x.BasePrice, x.Currency, x.IsActive, x.DisplayOrder))
                .ToList();
        }

        // DB chưa seed: trả về danh mục mặc định trong code.
        return SeatTypeBasePriceSupport.KnownCodes
            .Select((code, index) => new SeatTypeAdminDto(
                null, code, code, SeatTypePricing.GetBasePrice(code), "VND", true, index + 1))
            .ToList();
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record CreateSeatTypeCommand(
    string Code,
    string Name,
    decimal BasePrice,
    int? DisplayOrder = null) : IRequest<SeatTypeAdminDto>;

public sealed class CreateSeatTypeCommandValidator : AbstractValidator<CreateSeatTypeCommand>
{
    public CreateSeatTypeCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(50)
            .Matches("^[A-Za-z][A-Za-z0-9_]*$")
            .WithMessage("Code chỉ gồm chữ, số và dấu gạch dưới, bắt đầu bằng chữ (vd: VIP, FAMILY_DECK).");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.BasePrice)
            .GreaterThan(0).WithMessage("Giá gốc phải lớn hơn 0.")
            .LessThanOrEqualTo(100_000_000).WithMessage("Giá gốc tối đa 100.000.000 VND.")
            .Must(x => decimal.Truncate(x) == x).WithMessage("Giá gốc phải là số nguyên VND.");
        RuleFor(x => x.DisplayOrder).GreaterThan(0).When(x => x.DisplayOrder.HasValue);
    }
}

public sealed class CreateSeatTypeCommandHandler : IRequestHandler<CreateSeatTypeCommand, SeatTypeAdminDto>
{
    private readonly IApplicationDbContext _context;

    public CreateSeatTypeCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<SeatTypeAdminDto> Handle(
        CreateSeatTypeCommand request,
        CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

        if (await _context.Set<SeatType>().AnyAsync(x => x.Code == code, cancellationToken))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Code),
                $"Loại ghế '{code}' đã tồn tại. Dùng PUT /api/seat-types/{code} để chỉnh giá.")]);
        }

        var displayOrder = request.DisplayOrder
            ?? (await _context.Set<SeatType>().MaxAsync(x => (int?)x.DisplayOrder, cancellationToken) ?? 0) + 1;

        var seatType = new SeatType
        {
            Code = code,
            Name = request.Name.Trim(),
            BasePrice = request.BasePrice,
            Currency = "VND",
            IsActive = true,
            DisplayOrder = displayOrder
        };
        _context.Set<SeatType>().Add(seatType);
        await _context.SaveChangesAsync(cancellationToken);

        return new SeatTypeAdminDto(
            seatType.Id,
            seatType.Code,
            seatType.Name,
            seatType.BasePrice,
            seatType.Currency,
            seatType.IsActive,
            seatType.DisplayOrder);
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record UpdateSeatTypePriceCommand(string Code, decimal BasePrice)
    : IRequest<SeatTypeAdminDto>;

public sealed class UpdateSeatTypePriceCommandValidator : AbstractValidator<UpdateSeatTypePriceCommand>
{
    public UpdateSeatTypePriceCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.BasePrice)
            .GreaterThan(0).WithMessage("Giá gốc phải lớn hơn 0.")
            .LessThanOrEqualTo(100_000_000).WithMessage("Giá gốc tối đa 100.000.000 VND.")
            .Must(x => decimal.Truncate(x) == x).WithMessage("Giá gốc phải là số nguyên VND.");
    }
}

public sealed class UpdateSeatTypePriceCommandHandler
    : IRequestHandler<UpdateSeatTypePriceCommand, SeatTypeAdminDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateSeatTypePriceCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<SeatTypeAdminDto> Handle(
        UpdateSeatTypePriceCommand request,
        CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

        var seatType = await _context.Set<SeatType>()
            .SingleOrDefaultAsync(x => x.Code == code, cancellationToken);

        if (seatType is null && !SeatTypeBasePriceSupport.KnownCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
        {
            throw new NotFoundException(
                $"Seat type '{code}' not found. Tạo mới bằng POST /api/seat-types hoặc dùng: STANDARD, CABIN, RIVER, SKY.");
        }

        if (seatType is null)
        {
            // DB chưa có dòng này (chưa seed) → tạo mới từ danh mục mặc định.
            seatType = new SeatType
            {
                Code = code,
                Name = code,
                Currency = "VND",
                IsActive = true,
                DisplayOrder = Array.FindIndex(SeatTypeBasePriceSupport.KnownCodes,
                    x => x.Equals(code, StringComparison.OrdinalIgnoreCase)) + 1
            };
            _context.Set<SeatType>().Add(seatType);
        }

        seatType.BasePrice = request.BasePrice;
        await _context.SaveChangesAsync(cancellationToken);

        // Gắn lại seat_type_id cho các ghế cùng code chưa được link (ghế tạo trước khi seed).
        var unlinkedSeats = await _context.Set<Seat>()
            .Where(x => x.SeatTypeId == null && x.SeatTypeCode == code)
            .ToListAsync(cancellationToken);
        if (unlinkedSeats.Count > 0)
        {
            foreach (var seat in unlinkedSeats)
            {
                seat.SeatTypeId = seatType.Id;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        return new SeatTypeAdminDto(
            seatType.Id,
            seatType.Code,
            seatType.Name,
            seatType.BasePrice,
            seatType.Currency,
            seatType.IsActive,
            seatType.DisplayOrder);
    }
}
