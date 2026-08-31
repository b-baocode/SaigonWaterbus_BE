using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Notifications;

public sealed record NotificationDto(
    Guid NotificationId,
    string Title,
    string? Body,
    string Type,
    bool IsRead,
    DateTimeOffset? ReadAt,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    DateTimeOffset CreatedAt);

public sealed record NotificationListDto(
    IReadOnlyList<NotificationDto> Items,
    int TotalCount,
    int UnreadCount,
    int Page,
    int PageSize);

public sealed record UnreadNotificationCountDto(int UnreadCount);

public sealed record MarkAllNotificationsReadResultDto(int MarkedCount);

public sealed record MarkNotificationsReadByFilterCommand(
    string? Type = null,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null,
    bool UnreadOnly = true) : IRequest<MarkNotificationsReadByFilterResultDto>;

public sealed class MarkNotificationsReadByFilterCommandValidator : AbstractValidator<MarkNotificationsReadByFilterCommand>
{
    public MarkNotificationsReadByFilterCommandValidator()
    {
        RuleFor(x => x.Type).MaximumLength(50).When(x => x.Type is not null);
        RuleFor(x => x.RelatedEntityType).MaximumLength(50).When(x => x.RelatedEntityType is not null);
    }
}

public sealed record MarkNotificationsReadByFilterResultDto(int MarkedCount);

public sealed record GetMyNotificationsQuery(
    int Page = 1,
    int PageSize = 20,
    bool UnreadOnly = false,
    string? Type = null,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null) : IRequest<NotificationListDto>;

public sealed class GetMyNotificationsQueryValidator : AbstractValidator<GetMyNotificationsQuery>
{
    public GetMyNotificationsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Type).MaximumLength(50).When(x => x.Type is not null);
        RuleFor(x => x.RelatedEntityType).MaximumLength(50).When(x => x.RelatedEntityType is not null);
    }
}

public sealed class GetMyNotificationsQueryHandler
    : IRequestHandler<GetMyNotificationsQuery, NotificationListDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetMyNotificationsQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<NotificationListDto> Handle(
        GetMyNotificationsQuery request,
        CancellationToken cancellationToken)
    {
        var userId = NotificationApiSupport.GetRequiredUserId(_userContext);
        var myNotifications = _context.Set<Notification>()
            .AsNoTracking()
            .Where(n => n.UserId == userId);

        // Apply filters
        if (!string.IsNullOrWhiteSpace(request.Type))
            myNotifications = myNotifications.Where(n => n.Type == request.Type);

        if (!string.IsNullOrWhiteSpace(request.RelatedEntityType))
            myNotifications = myNotifications.Where(n => n.RelatedEntityType == request.RelatedEntityType);

        if (request.RelatedEntityId.HasValue)
            myNotifications = myNotifications.Where(n => n.RelatedEntityId == request.RelatedEntityId);

        var unreadCount = await myNotifications.CountAsync(n => !n.IsRead, cancellationToken);
        var filtered = request.UnreadOnly
            ? myNotifications.Where(n => !n.IsRead)
            : myNotifications;
        var totalCount = request.UnreadOnly
            ? unreadCount
            : await filtered.CountAsync(cancellationToken);

        var items = await filtered
            .OrderByDescending(n => n.CreatedAt)
            .ThenBy(n => n.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new NotificationListDto(
            items.Select(NotificationApiSupport.ToDto).ToList(),
            totalCount,
            unreadCount,
            request.Page,
            request.PageSize);
    }
}

public sealed record GetMyUnreadNotificationCountQuery : IRequest<UnreadNotificationCountDto>;

public sealed class GetMyUnreadNotificationCountQueryHandler
    : IRequestHandler<GetMyUnreadNotificationCountQuery, UnreadNotificationCountDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetMyUnreadNotificationCountQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<UnreadNotificationCountDto> Handle(
        GetMyUnreadNotificationCountQuery request,
        CancellationToken cancellationToken)
    {
        var userId = NotificationApiSupport.GetRequiredUserId(_userContext);
        var unreadCount = await _context.Set<Notification>()
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);
        return new UnreadNotificationCountDto(unreadCount);
    }
}

public sealed record MarkNotificationReadCommand(Guid NotificationId) : IRequest<NotificationDto>;

public sealed class MarkNotificationReadCommandValidator : AbstractValidator<MarkNotificationReadCommand>
{
    public MarkNotificationReadCommandValidator()
    {
        RuleFor(x => x.NotificationId).NotEmpty();
    }
}

public sealed class MarkNotificationReadCommandHandler
    : IRequestHandler<MarkNotificationReadCommand, NotificationDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public MarkNotificationReadCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<NotificationDto> Handle(
        MarkNotificationReadCommand request,
        CancellationToken cancellationToken)
    {
        var userId = NotificationApiSupport.GetRequiredUserId(_userContext);
        var notification = await _context.Set<Notification>()
            .SingleOrDefaultAsync(n => n.Id == request.NotificationId && n.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Notification not found.");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = _timeProvider.GetUtcNow();
            await _context.SaveChangesAsync(cancellationToken);
        }

        return NotificationApiSupport.ToDto(notification);
    }
}

public sealed record MarkAllMyNotificationsReadCommand : IRequest<MarkAllNotificationsReadResultDto>;

public sealed class MarkAllMyNotificationsReadCommandHandler
    : IRequestHandler<MarkAllMyNotificationsReadCommand, MarkAllNotificationsReadResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public MarkAllMyNotificationsReadCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<MarkAllNotificationsReadResultDto> Handle(
        MarkAllMyNotificationsReadCommand request,
        CancellationToken cancellationToken)
    {
        var userId = NotificationApiSupport.GetRequiredUserId(_userContext);
        var unreadNotifications = await _context.Set<Notification>()
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(cancellationToken);

        if (unreadNotifications.Count == 0)
        {
            return new MarkAllNotificationsReadResultDto(0);
        }

        var now = _timeProvider.GetUtcNow();
        foreach (var notification in unreadNotifications)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new MarkAllNotificationsReadResultDto(unreadNotifications.Count);
    }
}

public sealed class MarkNotificationsReadByFilterCommandHandler
    : IRequestHandler<MarkNotificationsReadByFilterCommand, MarkNotificationsReadByFilterResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public MarkNotificationsReadByFilterCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<MarkNotificationsReadByFilterResultDto> Handle(
        MarkNotificationsReadByFilterCommand request,
        CancellationToken cancellationToken)
    {
        var userId = NotificationApiSupport.GetRequiredUserId(_userContext);

        var query = _context.Set<Notification>()
            .Where(n => n.UserId == userId);

        if (request.UnreadOnly)
            query = query.Where(n => !n.IsRead);

        if (!string.IsNullOrWhiteSpace(request.Type))
            query = query.Where(n => n.Type == request.Type);

        if (!string.IsNullOrWhiteSpace(request.RelatedEntityType))
            query = query.Where(n => n.RelatedEntityType == request.RelatedEntityType);

        if (request.RelatedEntityId.HasValue)
            query = query.Where(n => n.RelatedEntityId == request.RelatedEntityId);

        var unreadNotifications = await query.ToListAsync(cancellationToken);

        if (unreadNotifications.Count == 0)
            return new MarkNotificationsReadByFilterResultDto(0);

        var now = _timeProvider.GetUtcNow();
        foreach (var notification in unreadNotifications)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new MarkNotificationsReadByFilterResultDto(unreadNotifications.Count);
    }
}

internal static class NotificationApiSupport
{
    public static Guid GetRequiredUserId(IUserContext userContext) =>
        userContext.UserId
        ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

    public static NotificationDto ToDto(Notification notification) =>
        new(
            notification.Id,
            notification.Title,
            notification.Body,
            notification.Type,
            notification.IsRead,
            notification.ReadAt,
            notification.RelatedEntityType,
            notification.RelatedEntityId,
            notification.CreatedAt);
}
