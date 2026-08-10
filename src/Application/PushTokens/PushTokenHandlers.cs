using MediatR;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.PushTokens;

public sealed class RegisterPushTokenCommandHandler
    : IRequestHandler<RegisterPushTokenCommand, RegisterPushTokenResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public RegisterPushTokenCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<RegisterPushTokenResult> Handle(
        RegisterPushTokenCommand request,
        CancellationToken cancellationToken)
    {
        var userId = GetRequiredUserId();
        var now = _timeProvider.GetUtcNow();

        // 1) Nếu token đã tồn tại cho bất kỳ user nào → gán lại cho user hiện tại và reactivate.
        var existing = await _context.Set<UserPushToken>()
            .SingleOrDefaultAsync(t => t.ExpoPushToken == request.ExpoPushToken, cancellationToken);

        if (existing is not null)
        {
            if (existing.UserId != userId || !existing.IsActive)
            {
                existing.UserId = userId;
                existing.IsActive = true;
                existing.DisabledAt = null;
                existing.Platform = request.Platform;
                existing.DeviceId = request.DeviceId;
                existing.AppVersion = request.AppVersion;
                existing.UpdatedAt = now;
                await _context.SaveChangesAsync(cancellationToken);
                return new RegisterPushTokenResult(existing.Id, AlreadyRegistered: false);
            }
            return new RegisterPushTokenResult(existing.Id, AlreadyRegistered: true);
        }

        // 2) Token mới: nếu cùng deviceId đã có token khác của user → deactivate token cũ.
        if (!string.IsNullOrWhiteSpace(request.DeviceId))
        {
            var previousOnSameDevice = await _context.Set<UserPushToken>()
                .Where(t => t.UserId == userId
                    && t.DeviceId == request.DeviceId
                    && t.IsActive
                    && t.ExpoPushToken != request.ExpoPushToken)
                .ToListAsync(cancellationToken);

            foreach (var prev in previousOnSameDevice)
            {
                prev.IsActive = false;
                prev.DisabledAt = now;
                prev.UpdatedAt = now;
            }
        }

        var token = new UserPushToken
        {
            UserId = userId,
            ExpoPushToken = request.ExpoPushToken,
            Platform = request.Platform,
            DeviceId = request.DeviceId,
            AppVersion = request.AppVersion,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.Set<UserPushToken>().Add(token);
        await _context.SaveChangesAsync(cancellationToken);

        return new RegisterPushTokenResult(token.Id, AlreadyRegistered: false);
    }

    private Guid GetRequiredUserId() =>
        _userContext.UserId
        ?? throw new Application.Common.Exceptions.ValidationException([new FluentValidation.Results.ValidationFailure("userId", "User must be authenticated.")]);
}

public sealed class UnregisterPushTokenCommandHandler
    : IRequestHandler<UnregisterPushTokenCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public UnregisterPushTokenCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<bool> Handle(
        UnregisterPushTokenCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new Application.Common.Exceptions.ValidationException([new FluentValidation.Results.ValidationFailure("userId", "User must be authenticated.")]);

        var token = await _context.Set<UserPushToken>()
            .SingleOrDefaultAsync(t => t.Id == request.Id && t.UserId == userId, cancellationToken);

        if (token is null) return false;

        _context.Set<UserPushToken>().Remove(token);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed class DisableMyPushTokensCommandHandler
    : IRequestHandler<DisableMyPushTokensCommand, int>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public DisableMyPushTokensCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<int> Handle(
        DisableMyPushTokensCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new Application.Common.Exceptions.ValidationException([new FluentValidation.Results.ValidationFailure("userId", "User must be authenticated.")]);

        var now = _timeProvider.GetUtcNow();
        var tokens = await _context.Set<UserPushToken>()
            .Where(t => t.UserId == userId && t.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.IsActive = false;
            token.DisabledAt = now;
            token.UpdatedAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return tokens.Count;
    }
}
