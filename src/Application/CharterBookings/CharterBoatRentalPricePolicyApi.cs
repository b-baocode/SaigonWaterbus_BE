using FluentValidation.Results;
using SaigonWaterbus.Application.Boats;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record CharterBoatRentalPricePolicyDto(
    Guid? CharterBoatRentalPricePolicyId,
    int NumberOfDecks,
    BoatRentalUnit RentalUnit,
    decimal UnitPrice,
    string Currency);

[Authorize(Roles = "Admin,Manager")]
public sealed record GetCharterBoatRentalPricePolicyListQuery
    : IRequest<IReadOnlyList<CharterBoatRentalPricePolicyDto>>;

[Authorize(Roles = "Admin,Manager")]
public sealed record UpsertCharterBoatRentalPricePolicyCommand(
    int NumberOfDecks,
    BoatRentalUnit RentalUnit,
    decimal UnitPrice,
    string? Currency = null) : IRequest<CharterBoatRentalPricePolicyDto>;

public sealed class UpsertCharterBoatRentalPricePolicyCommandValidator
    : AbstractValidator<UpsertCharterBoatRentalPricePolicyCommand>
{
    public UpsertCharterBoatRentalPricePolicyCommandValidator()
    {
        RuleFor(x => x.NumberOfDecks).GreaterThan(0).LessThanOrEqualTo(10);
        RuleFor(x => x.RentalUnit).IsInEnum();
        RuleFor(x => x.UnitPrice)
            .GreaterThanOrEqualTo(0).WithMessage("Giá thuê tàu không được âm.")
            .LessThanOrEqualTo(1_000_000_000).WithMessage("Giá thuê tàu tối đa 1.000.000.000 VND.")
            .Must(x => decimal.Truncate(x) == x).WithMessage("Giá thuê tàu phải là số nguyên VND.");
        RuleFor(x => x.Currency)
            .Must(BoatSupport.IsValidCurrencyCode)
            .WithMessage("Currency phải là mã ISO 4217 gồm 3 chữ cái, ví dụ VND.")
            .When(x => x.Currency is not null);
    }
}

public sealed class GetCharterBoatRentalPricePolicyListQueryHandler
    : IRequestHandler<GetCharterBoatRentalPricePolicyListQuery, IReadOnlyList<CharterBoatRentalPricePolicyDto>>
{
    private readonly IApplicationDbContext _context;

    public GetCharterBoatRentalPricePolicyListQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<CharterBoatRentalPricePolicyDto>> Handle(
        GetCharterBoatRentalPricePolicyListQuery request,
        CancellationToken cancellationToken)
    {
        var rows = await _context.Set<CharterBoatRentalPricePolicy>()
            .AsNoTracking()
            .OrderBy(x => x.NumberOfDecks)
            .ThenBy(x => x.RentalUnit)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDto).ToArray();
    }

    private static CharterBoatRentalPricePolicyDto ToDto(CharterBoatRentalPricePolicy policy) =>
        new(policy.Id, policy.NumberOfDecks, policy.RentalUnit, policy.UnitPrice, policy.Currency);
}

public sealed class UpsertCharterBoatRentalPricePolicyCommandHandler
    : IRequestHandler<UpsertCharterBoatRentalPricePolicyCommand, CharterBoatRentalPricePolicyDto>
{
    private readonly IApplicationDbContext _context;

    public UpsertCharterBoatRentalPricePolicyCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<CharterBoatRentalPricePolicyDto> Handle(
        UpsertCharterBoatRentalPricePolicyCommand request,
        CancellationToken cancellationToken)
    {
        var policy = await _context.Set<CharterBoatRentalPricePolicy>()
            .SingleOrDefaultAsync(x => x.NumberOfDecks == request.NumberOfDecks
                && x.RentalUnit == request.RentalUnit, cancellationToken);

        if (policy is null)
        {
            policy = new CharterBoatRentalPricePolicy
            {
                NumberOfDecks = request.NumberOfDecks,
                RentalUnit = request.RentalUnit
            };
            _context.Set<CharterBoatRentalPricePolicy>().Add(policy);
        }

        policy.UnitPrice = request.UnitPrice;
        policy.Currency = BoatSupport.NormalizeCurrency(request.Currency);

        await _context.SaveChangesAsync(cancellationToken);

        return ToDto(policy);
    }

    private static CharterBoatRentalPricePolicyDto ToDto(CharterBoatRentalPricePolicy policy) =>
        new(policy.Id, policy.NumberOfDecks, policy.RentalUnit, policy.UnitPrice, policy.Currency);
}
