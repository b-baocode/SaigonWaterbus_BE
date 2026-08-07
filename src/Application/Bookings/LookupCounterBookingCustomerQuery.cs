using System.ComponentModel.DataAnnotations;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Bookings;

public sealed record CounterBookingCustomerLookupDto(
    Guid CustomerUserId,
    string FullName,
    string? PhoneNumber,
    string? Email,
    int PointBalance);

public sealed record LookupCounterBookingCustomerQuery(string Keyword)
    : IRequest<IReadOnlyList<CounterBookingCustomerLookupDto>>;

public sealed class LookupCounterBookingCustomerQueryValidator
    : AbstractValidator<LookupCounterBookingCustomerQuery>
{
    public LookupCounterBookingCustomerQueryValidator()
    {
        RuleFor(x => x.Keyword)
            .NotEmpty()
            .MaximumLength(255)
            .Must(BePhoneOrEmail)
            .WithMessage("Từ khóa phải là số điện thoại hoặc email.");
    }

    private static bool BePhoneOrEmail(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        var trimmed = keyword.Trim();
        return PhoneRules.IsValid(trimmed) || IsEmail(trimmed);
    }

    private static bool IsEmail(string value) =>
        new EmailAddressAttribute().IsValid(value);
}

public sealed class LookupCounterBookingCustomerQueryHandler
    : IRequestHandler<LookupCounterBookingCustomerQuery, IReadOnlyList<CounterBookingCustomerLookupDto>>
{
    private const int MaxResults = 5;

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public LookupCounterBookingCustomerQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<CounterBookingCustomerLookupDto>> Handle(
        LookupCounterBookingCustomerQuery request,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserCanSellAtCounterAsync(
            _context, _userContext, cancellationToken);

        var keyword = request.Keyword.Trim();
        var normalizedEmail = IsEmail(keyword)
            ? keyword.ToUpperInvariant()
            : null;
        var normalizedPhone = PhoneRules.TryNormalize(keyword, out var phone)
            ? phone
            : null;

        return await _context.Set<User>()
            .Where(u => u.Status == UserStatus.Active
                && u.Role.Code == Roles.CustomerCode
                && ((normalizedEmail != null
                        && ((u.NormalizedEmail != null && u.NormalizedEmail == normalizedEmail)
                            || (u.Email != null && u.Email.ToUpper() == normalizedEmail)))
                    || (normalizedPhone != null
                        && (u.NormalizedPhoneNumber == normalizedPhone || u.PhoneNumber == normalizedPhone))))
            .OrderBy(u => u.FullName)
            .Take(MaxResults)
            .Select(u => new CounterBookingCustomerLookupDto(
                u.Id,
                u.FullName,
                u.PhoneNumber,
                u.Email,
                u.PointBalance))
            .ToListAsync(cancellationToken);
    }

    private static bool IsEmail(string value) =>
        new EmailAddressAttribute().IsValid(value);
}
