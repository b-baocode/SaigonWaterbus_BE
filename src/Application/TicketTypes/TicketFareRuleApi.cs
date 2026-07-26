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

[Authorize(Roles = "Admin,Manager")]
public sealed record GetTicketFareRuleListQuery : IRequest<IReadOnlyList<TicketFareRuleDto>>;

[Authorize(Roles = "Admin,Manager")]
public sealed record UpdateTicketFareRuleCommand(
    string TicketTypeCode,
    string RouteType,
    decimal PriceModifier,
    bool IsActive = true) : IRequest<TicketFareRuleDto>;

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
                var key = (ticketType.Code, routeType);
                return configuredRules.TryGetValue(key, out var configured)
                    ? ToDto(configured, ticketType)
                    : new TicketFareRuleDto(
                        null,
                        ticketType.Code,
                        ticketType.Name,
                        routeType,
                        ticketType.GetPriceModifier(routeType),
                        true);
            }))
            .ToArray();

    public static decimal GetMinimumPositivePriceModifier(
        IReadOnlyDictionary<(string TicketTypeCode, string RouteType), TicketFareRule> configuredRules,
        string routeType) =>
        TicketTypePricing.All
            .Select(ticketType => configuredRules.TryGetValue((ticketType.Code, routeType), out var configured)
                && configured.IsActive
                    ? configured.PriceModifier
                    : ticketType.GetPriceModifier(routeType))
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
