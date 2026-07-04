using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Common.Security;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Common.Behaviours;

public sealed class AuthorizationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public AuthorizationBehaviour(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var authorizeAttribute = typeof(TRequest)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .FirstOrDefault();

        if (authorizeAttribute is not null)
        {
            var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
            if (!HasAnyRole(actor, authorizeAttribute.Roles))
            {
                throw new ForbiddenAccessException();
            }
        }

        return await next(cancellationToken);
    }

    private static bool HasAnyRole(User actor, string roles) =>
        roles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(role => role.ToUpperInvariant() switch
            {
                "ADMIN" => AuthSupport.IsAdmin(actor),
                "MANAGER" => AuthSupport.IsManager(actor),
                "STAFF" => AuthSupport.IsStaff(actor),
                "CUSTOMER" => AuthSupport.IsCustomer(actor),
                _ => false
            });
}
