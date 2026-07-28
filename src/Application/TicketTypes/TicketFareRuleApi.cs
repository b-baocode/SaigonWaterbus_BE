using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.TicketTypes;

public sealed record TicketFareRuleDto(
    Guid? TicketFareRuleId,
    string TicketTypeCode,
    string TicketTypeName,
    string RouteType,
    decimal PriceModifier,
    bool IsActive);

public sealed record SightseeingConcessionFareRuleDto(
    decimal DiscountPercent,
    decimal PriceModifier,
    IReadOnlyList<string> TicketTypeCodes,
    string RouteType);

[Authorize(Roles = "Admin,Manager")]
public sealed record GetTicketFareRuleListQuery : IRequest<IReadOnlyList<TicketFareRuleDto>>;

[Authorize(Roles = "Admin,Manager")]
public sealed record UpdateTicketFareRuleCommand(
    string TicketTypeCode,
    string RouteType,
    decimal PriceModifier,
    bool IsActive = true) : IRequest<TicketFareRuleDto>;

[Authorize(Roles = "Admin,Manager")]
public sealed record GetSightseeingConcessionFareRuleQuery : IRequest<SightseeingConcessionFareRuleDto>;

[Authorize(Roles = "Admin,Manager")]
public sealed record UpdateSightseeingConcessionFareRuleCommand(
    decimal DiscountPercent) : IRequest<SightseeingConcessionFareRuleDto>;

public sealed class UpdateTicketFareRuleCommandValidator : AbstractValidator<UpdateTicketFareRuleCommand>
{
    public UpdateTicketFareRuleCommandValidator()
    {
        RuleFor(x => x.TicketTypeCode).NotEmpty().MaximumLength(30);
        RuleFor(x => x.RouteType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PriceModifier)
            .GreaterThanOrEqualTo(0).WithMessage("Hệ số giá không được âm.")
            .LessThanOrEqualTo(1).WithMessage("Hệ số giá tối đa là 1. Dùng fare adjustment nếu cần phụ thu.");
    }
}

public sealed class UpdateSightseeingConcessionFareRuleCommandValidator
    : AbstractValidator<UpdateSightseeingConcessionFareRuleCommand>
{
    public UpdateSightseeingConcessionFareRuleCommandValidator()
    {
        RuleFor(x => x.DiscountPercent)
            .GreaterThanOrEqualTo(0).WithMessage("Phần trăm giảm không được âm.")
            .LessThanOrEqualTo(100).WithMessage("Phần trăm giảm tối đa là 100%.")
            .Must(x => Math.Round(x, 2) == x).WithMessage("Phần trăm giảm chỉ nhận tối đa 2 chữ số thập phân.");
    }
}

public sealed class GetTicketFareRuleListQueryHandler
    : IRequestHandler<GetTicketFareRuleListQuery, IReadOnlyList<TicketFareRuleDto>>
{
    private readonly IApplicationDbContext _context;

    public GetTicketFareRuleListQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<TicketFareRuleDto>> Handle(
        GetTicketFareRuleListQuery request,
        CancellationToken cancellationToken)
    {
        var configuredRules = await TicketFareRuleSupport.LoadConfiguredRulesAsync(_context, cancellationToken);
        return TicketFareRuleSupport.BuildRuleDtos(configuredRules);
    }
}

public sealed class UpdateTicketFareRuleCommandHandler
    : IRequestHandler<UpdateTicketFareRuleCommand, TicketFareRuleDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateTicketFareRuleCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<TicketFareRuleDto> Handle(
        UpdateTicketFareRuleCommand request,
        CancellationToken cancellationToken)
    {
        var ticketType = TicketFareRuleSupport.ResolveTicketType(request.TicketTypeCode);
        var routeType = TicketFareRuleSupport.NormalizeBookableRouteType(request.RouteType);

        if (TicketFareRuleSupport.IsFixedFreeRule(ticketType.Code, routeType))
        {
            await TicketFareRuleSupport.RemoveConfiguredRuleAsync(
                _context,
                ticketType.Code,
                routeType,
                cancellationToken);

            return TicketFareRuleSupport.BuildFixedFreeRuleDto(ticketType, routeType);
        }

        if (TicketFareRuleSupport.IsSightseeingConcessionRule(ticketType.Code, routeType))
        {
            await TicketFareRuleSupport.UpsertSightseeingConcessionRulesAsync(
                _context,
                request.PriceModifier,
                request.IsActive,
                cancellationToken);

            var configuredRules = await TicketFareRuleSupport.LoadConfiguredRulesAsync(_context, cancellationToken);
            return TicketFareRuleSupport.BuildRuleDtos(configuredRules)
                .Single(x => x.TicketTypeCode == ticketType.Code && x.RouteType == routeType);
        }

        var rule = await _context.Set<TicketFareRule>()
            .SingleOrDefaultAsync(x => x.TicketTypeCode == ticketType.Code && x.RouteType == routeType, cancellationToken);

        if (rule is null)
        {
            rule = new TicketFareRule
            {
                TicketTypeCode = ticketType.Code,
                RouteType = routeType
            };
            _context.Set<TicketFareRule>().Add(rule);
        }

        rule.PriceModifier = request.PriceModifier;
        rule.IsActive = request.IsActive;

        await _context.SaveChangesAsync(cancellationToken);

        return TicketFareRuleSupport.ToDto(rule, ticketType);
    }
}

public sealed class GetSightseeingConcessionFareRuleQueryHandler
    : IRequestHandler<GetSightseeingConcessionFareRuleQuery, SightseeingConcessionFareRuleDto>
{
    private readonly IApplicationDbContext _context;

    public GetSightseeingConcessionFareRuleQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<SightseeingConcessionFareRuleDto> Handle(
        GetSightseeingConcessionFareRuleQuery request,
        CancellationToken cancellationToken)
    {
        var configuredRules = await TicketFareRuleSupport.LoadConfiguredRulesAsync(_context, cancellationToken);
        return TicketFareRuleSupport.BuildSightseeingConcessionDto(configuredRules);
    }
}

public sealed class UpdateSightseeingConcessionFareRuleCommandHandler
    : IRequestHandler<UpdateSightseeingConcessionFareRuleCommand, SightseeingConcessionFareRuleDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateSightseeingConcessionFareRuleCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<SightseeingConcessionFareRuleDto> Handle(
        UpdateSightseeingConcessionFareRuleCommand request,
        CancellationToken cancellationToken)
    {
        var priceModifier = TicketFareRuleSupport.DiscountPercentToPriceModifier(request.DiscountPercent);
        await TicketFareRuleSupport.UpsertSightseeingConcessionRulesAsync(
            _context,
            priceModifier,
            isActive: true,
            cancellationToken);

        var configuredRules = await TicketFareRuleSupport.LoadConfiguredRulesAsync(_context, cancellationToken);
        return TicketFareRuleSupport.BuildSightseeingConcessionDto(configuredRules);
    }
}

public static class TicketFareRuleSupport
{
    private static readonly string[] BookableRouteTypes = [RouteTypes.Regular, RouteTypes.SightseeingLoop];

    public static TicketTypeInfo ResolveTicketType(string ticketTypeCode)
    {
        if (TicketTypePricing.TryGet(ticketTypeCode, out var ticketType))
        {
            return ticketType;
        }

        throw new NotFoundException($"Ticket type '{ticketTypeCode}' not found.");
    }

    public static string NormalizeBookableRouteType(string? routeType)
    {
        var normalized = RouteTypes.Normalize(routeType);
        if (BookableRouteTypes.Contains(normalized, StringComparer.Ordinal))
        {
            return normalized;
        }

        throw new ValidationException(
        [
            new ValidationFailure(nameof(routeType),
                "routeType chỉ nhận Regular hoặc SightseeingLoop cho giá vé lẻ.")
        ]);
    }

    public static async Task<decimal> GetEffectivePriceModifierAsync(
        IApplicationDbContext context,
        TicketTypeInfo ticketType,
        string? routeType,
        CancellationToken cancellationToken)
    {
        var normalizedRouteType = RouteTypes.Normalize(routeType);
        if (IsFixedFreeRule(ticketType.Code, normalizedRouteType))
        {
            return 0m;
        }

        if (IsSightseeingConcessionRule(ticketType.Code, normalizedRouteType))
        {
            var configuredRules = await context.Set<TicketFareRule>()
                .AsNoTracking()
                .Where(x => x.IsActive
                    && x.RouteType == RouteTypes.SightseeingLoop
                    && TicketTypePricing.SightseeingConcessionTicketTypeCodes.Contains(x.TicketTypeCode))
                .Select(x => new { x.TicketTypeCode, x.PriceModifier })
                .ToListAsync(cancellationToken);

            foreach (var ticketTypeCode in TicketTypePricing.SightseeingConcessionTicketTypeCodes)
            {
                var configuredRule = configuredRules.SingleOrDefault(x => x.TicketTypeCode == ticketTypeCode);
                if (configuredRule is not null)
                {
                    return configuredRule.PriceModifier;
                }
            }

            return TicketTypePricing.SightseeingConcessionPriceModifier;
        }

        var configuredModifier = await context.Set<TicketFareRule>()
            .AsNoTracking()
            .Where(x => x.IsActive
                && x.TicketTypeCode == ticketType.Code
                && x.RouteType == normalizedRouteType)
            .Select(x => (decimal?)x.PriceModifier)
            .SingleOrDefaultAsync(cancellationToken);

        return configuredModifier ?? ticketType.GetPriceModifier(normalizedRouteType);
    }

    public static async Task<IReadOnlyDictionary<(string TicketTypeCode, string RouteType), TicketFareRule>>
        LoadConfiguredRulesAsync(IApplicationDbContext context, CancellationToken cancellationToken)
    {
        var rows = await context.Set<TicketFareRule>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            x => (x.TicketTypeCode, x.RouteType),
            StringTupleComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<TicketFareRuleDto> BuildRuleDtos(
        IReadOnlyDictionary<(string TicketTypeCode, string RouteType), TicketFareRule> configuredRules) =>
        TicketTypePricing.All
            .SelectMany(ticketType => BookableRouteTypes.Select(routeType =>
            {
                if (IsFixedFreeRule(ticketType.Code, routeType))
                {
                    return BuildFixedFreeRuleDto(ticketType, routeType);
                }

                var key = (ticketType.Code, routeType);
                return configuredRules.TryGetValue(key, out var configured)
                    ? ToDto(configured, ticketType) with
                    {
                        PriceModifier = ResolveEffectivePriceModifier(configuredRules, ticketType, routeType)
                    }
                    : new TicketFareRuleDto(
                        null,
                        ticketType.Code,
                        ticketType.Name,
                        routeType,
                        ResolveEffectivePriceModifier(configuredRules, ticketType, routeType),
                        true);
            }))
            .ToArray();

    public static decimal GetMinimumPositivePriceModifier(
        IReadOnlyDictionary<(string TicketTypeCode, string RouteType), TicketFareRule> configuredRules,
        string routeType) =>
        TicketTypePricing.All
            .Select(ticketType => ResolveEffectivePriceModifier(configuredRules, ticketType, routeType))
            .Where(x => x > 0)
            .Min();

    public static TicketFareRuleDto ToDto(TicketFareRule rule, TicketTypeInfo ticketType) =>
        new(
            rule.Id,
            rule.TicketTypeCode,
            ticketType.Name,
            rule.RouteType,
            rule.PriceModifier,
            rule.IsActive);

    public static TicketFareRuleDto BuildFixedFreeRuleDto(TicketTypeInfo ticketType, string routeType) =>
        new(
            null,
            ticketType.Code,
            ticketType.Name,
            routeType,
            0m,
            true);

    public static SightseeingConcessionFareRuleDto BuildSightseeingConcessionDto(
        IReadOnlyDictionary<(string TicketTypeCode, string RouteType), TicketFareRule> configuredRules)
    {
        var priceModifier = ResolveSightseeingConcessionPriceModifier(configuredRules);
        return new SightseeingConcessionFareRuleDto(
            PriceModifierToDiscountPercent(priceModifier),
            priceModifier,
            TicketTypePricing.SightseeingConcessionTicketTypeCodes,
            RouteTypes.SightseeingLoop);
    }

    public static decimal DiscountPercentToPriceModifier(decimal discountPercent) =>
        Math.Round((100m - discountPercent) / 100m, 4, MidpointRounding.AwayFromZero);

    public static async Task UpsertSightseeingConcessionRulesAsync(
        IApplicationDbContext context,
        decimal priceModifier,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var existingRules = await context.Set<TicketFareRule>()
            .Where(x => x.RouteType == RouteTypes.SightseeingLoop
                && TicketTypePricing.SightseeingConcessionTicketTypeCodes.Contains(x.TicketTypeCode))
            .ToListAsync(cancellationToken);

        foreach (var ticketTypeCode in TicketTypePricing.SightseeingConcessionTicketTypeCodes)
        {
            var rule = existingRules.SingleOrDefault(x => x.TicketTypeCode == ticketTypeCode);
            if (rule is null)
            {
                rule = new TicketFareRule
                {
                    TicketTypeCode = ticketTypeCode,
                    RouteType = RouteTypes.SightseeingLoop
                };
                context.Set<TicketFareRule>().Add(rule);
            }

            rule.PriceModifier = priceModifier;
            rule.IsActive = isActive;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public static async Task RemoveConfiguredRuleAsync(
        IApplicationDbContext context,
        string ticketTypeCode,
        string routeType,
        CancellationToken cancellationToken)
    {
        var rule = await context.Set<TicketFareRule>()
            .SingleOrDefaultAsync(x => x.TicketTypeCode == ticketTypeCode && x.RouteType == routeType, cancellationToken);

        if (rule is null)
        {
            return;
        }

        context.Set<TicketFareRule>().Remove(rule);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static decimal PriceModifierToDiscountPercent(decimal priceModifier) =>
        Math.Round((1m - priceModifier) * 100m, 2, MidpointRounding.AwayFromZero);

    public static decimal ResolveEffectivePriceModifier(
        IReadOnlyDictionary<(string TicketTypeCode, string RouteType), TicketFareRule> configuredRules,
        TicketTypeInfo ticketType,
        string routeType)
    {
        if (IsFixedFreeRule(ticketType.Code, routeType))
        {
            return 0m;
        }

        if (IsSightseeingConcessionRule(ticketType.Code, routeType))
        {
            return ResolveSightseeingConcessionPriceModifier(configuredRules);
        }

        return configuredRules.TryGetValue((ticketType.Code, routeType), out var configuredRule)
            && configuredRule.IsActive
                ? configuredRule.PriceModifier
                : ticketType.GetPriceModifier(routeType);
    }

    private static decimal ResolveSightseeingConcessionPriceModifier(
        IReadOnlyDictionary<(string TicketTypeCode, string RouteType), TicketFareRule> configuredRules)
    {
        foreach (var ticketTypeCode in TicketTypePricing.SightseeingConcessionTicketTypeCodes)
        {
            if (configuredRules.TryGetValue((ticketTypeCode, RouteTypes.SightseeingLoop), out var configuredRule)
                && configuredRule.IsActive)
            {
                return configuredRule.PriceModifier;
            }
        }

        return TicketTypePricing.SightseeingConcessionPriceModifier;
    }

    public static bool IsSightseeingConcessionRule(string ticketTypeCode, string routeType) =>
        string.Equals(routeType, RouteTypes.SightseeingLoop, StringComparison.Ordinal)
        && TicketTypePricing.IsSightseeingConcessionTicketType(ticketTypeCode);

    public static bool IsFixedFreeRule(string ticketTypeCode, string routeType)
    {
        var normalizedRouteType = RouteTypes.Normalize(routeType);
        return TicketTypePricing.IsAlwaysFreeTicketType(ticketTypeCode)
            || (string.Equals(normalizedRouteType, RouteTypes.Regular, StringComparison.Ordinal)
                && TicketTypePricing.IsFreeRegularTicketType(ticketTypeCode));
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string TicketTypeCode, string RouteType)>
    {
        public static readonly StringTupleComparer OrdinalIgnoreCase = new();

        public bool Equals(
            (string TicketTypeCode, string RouteType) x,
            (string TicketTypeCode, string RouteType) y) =>
            string.Equals(x.TicketTypeCode, y.TicketTypeCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.RouteType, y.RouteType, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string TicketTypeCode, string RouteType) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TicketTypeCode),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.RouteType));
    }
}
