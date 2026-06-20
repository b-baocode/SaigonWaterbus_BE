using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.CustomBookingRequests;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Operations;

public sealed record OperationScheduleItemDto(
    Guid Id,
    OperationScheduleSourceType SourceType,
    Guid SourceId,
    string SourceCode,
    string Title,
    Guid? ServiceId,
    string? ServiceCode,
    string? ServiceName,
    BookingMode? BookingMode,
    Guid? VesselId,
    string? VesselCode,
    string? VesselName,
    Guid? RouteId,
    string? RouteCode,
    string? RouteName,
    Guid? FromStationId,
    string? FromStationCode,
    string FromLocation,
    Guid? ToStationId,
    string? ToStationCode,
    string ToLocation,
    DateOnly OperatingDate,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Status,
    string ScheduleState,
    string OperationStatus,
    string? PaymentStage,
    DateTimeOffset? RemainingPaymentDeadline,
    bool IsPaymentOverdue,
    int DelayMinutes,
    string? DelayReason,
    DateTimeOffset? AdjustedStartAt,
    DateTimeOffset? AdjustedEndAt,
    DateTimeOffset? ActualStartAt,
    DateTimeOffset? ActualEndAt,
    DateTimeOffset SyncedAt);

public sealed record GetOperationScheduleQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    bool IncludeCancelled = false) : IRequest<IReadOnlyList<OperationScheduleItemDto>>;

public sealed class GetOperationScheduleQueryHandler
    : IRequestHandler<GetOperationScheduleQuery, IReadOnlyList<OperationScheduleItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetOperationScheduleQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<OperationScheduleItemDto>> Handle(
        GetOperationScheduleQuery request,
        CancellationToken cancellationToken)
    {
        var from = request.From.ToUniversalTime();
        var to = request.To.ToUniversalTime();
        if (to <= from)
        {
            throw AuthSupport.CreateValidationException(nameof(request.To), "Khoảng thời gian lịch không hợp lệ.");
        }

        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);

        var query = _context.OperationScheduleEntries
            .AsNoTracking()
            .Where(x => x.StartAt < to && x.EndAt > from);

        if (!request.IncludeCancelled)
        {
            query = query.Where(x => x.ScheduleState != OperationScheduleStates.Cancelled);
        }

        query = await OperationScheduleAccess.ApplyVisibilityAsync(
            _context,
            query,
            actor,
            cancellationToken);

        return await query
            .OrderBy(x => x.StartAt)
            .ThenBy(x => x.EndAt)
            .ThenBy(x => x.SourceCode)
            .Select(x => new OperationScheduleItemDto(
                x.Id,
                x.SourceType,
                x.SourceId,
                x.SourceCode,
                x.Title,
                x.ServiceId,
                x.ServiceCode,
                x.ServiceName,
                x.BookingMode,
                x.VesselId,
                x.VesselCode,
                x.VesselName,
                x.RouteId,
                x.RouteCode,
                x.RouteName,
                x.FromStationId,
                x.FromStationCode,
                x.FromLocation,
                x.ToStationId,
                x.ToStationCode,
                x.ToLocation,
                x.OperatingDate,
                x.StartAt,
                x.EndAt,
                x.Status,
                x.ScheduleState,
                x.OperationStatus,
                x.PaymentStage,
                x.RemainingPaymentDeadline,
                x.IsPaymentOverdue,
                x.DelayMinutes,
                x.DelayReason,
                x.AdjustedStartAt,
                x.AdjustedEndAt,
                x.ActualStartAt,
                x.ActualEndAt,
                x.SyncedAt))
            .ToArrayAsync(cancellationToken);
    }

}

public sealed record DelayOperationScheduleEntryCommand(
    Guid Id,
    int DelayMinutes,
    string Reason) : IRequest<OperationScheduleItemDto>;

public sealed class DelayOperationScheduleEntryCommandValidator
    : AbstractValidator<DelayOperationScheduleEntryCommand>
{
    public DelayOperationScheduleEntryCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id lịch vận hành không hợp lệ.");
        RuleFor(x => x.DelayMinutes)
            .InclusiveBetween(1, 1440)
            .WithMessage("Số phút delay phải từ 1 đến 1440.");
        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Lý do delay là bắt buộc.")
            .MaximumLength(500)
            .WithMessage("Lý do delay không được vượt quá 500 ký tự.");
    }
}

public sealed class DelayOperationScheduleEntryCommandHandler
    : IRequestHandler<DelayOperationScheduleEntryCommand, OperationScheduleItemDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public DelayOperationScheduleEntryCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<OperationScheduleItemDto> Handle(
        DelayOperationScheduleEntryCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(actor) && !AuthSupport.IsManager(actor) && !AuthSupport.IsStaff(actor))
        {
            throw new ForbiddenAccessException();
        }

        var entry = await _context.OperationScheduleEntries
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy lịch vận hành.");

        if (!await OperationScheduleAccess.CanManageEntryAsync(
                _context,
                entry,
                actor,
                cancellationToken))
        {
            throw new ForbiddenAccessException();
        }

        if (entry.ScheduleState == OperationScheduleStates.Cancelled
            || string.Equals(entry.OperationStatus, OperationStatuses.Cancelled, StringComparison.Ordinal))
        {
            throw AuthSupport.CreateValidationException(
                nameof(entry.OperationStatus),
                "Không thể delay lịch đã hủy.");
        }

        entry.DelayMinutes = request.DelayMinutes;
        entry.DelayReason = request.Reason.Trim();
        entry.AdjustedStartAt = entry.StartAt.AddMinutes(request.DelayMinutes);
        entry.AdjustedEndAt = entry.EndAt.AddMinutes(request.DelayMinutes);
        entry.OperationStatus = OperationStatuses.Delayed;

        await _context.SaveChangesAsync(cancellationToken);
        return OperationScheduleDtoFactory.Create(entry);
    }
}

public sealed record RefreshOperationScheduleResultDto(
    DateTimeOffset SyncedAt,
    int Count);

public sealed record RefreshOperationScheduleCommand(
    DateTimeOffset From,
    DateTimeOffset To) : IRequest<RefreshOperationScheduleResultDto>;

public sealed class RefreshOperationScheduleCommandHandler
    : IRequestHandler<RefreshOperationScheduleCommand, RefreshOperationScheduleResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IOperationScheduleSynchronizer _synchronizer;
    private readonly TimeProvider _timeProvider;

    public RefreshOperationScheduleCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IOperationScheduleSynchronizer synchronizer,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _synchronizer = synchronizer;
        _timeProvider = timeProvider;
    }

    public async Task<RefreshOperationScheduleResultDto> Handle(
        RefreshOperationScheduleCommand request,
        CancellationToken cancellationToken)
    {
        var from = request.From.ToUniversalTime();
        var to = request.To.ToUniversalTime();
        if (to <= from)
        {
            throw AuthSupport.CreateValidationException(nameof(request.To), "Khoảng thời gian đồng bộ lịch không hợp lệ.");
        }

        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(actor) && !AuthSupport.IsManager(actor) && !AuthSupport.IsStaff(actor))
        {
            throw new ForbiddenAccessException();
        }

        var count = await _synchronizer.SyncAsync(from, to, cancellationToken);
        return new RefreshOperationScheduleResultDto(_timeProvider.GetUtcNow(), count);
    }
}

internal static class OperationScheduleDtoFactory
{
    public static OperationScheduleItemDto Create(OperationScheduleEntry entry) =>
        new(
            entry.Id,
            entry.SourceType,
            entry.SourceId,
            entry.SourceCode,
            entry.Title,
            entry.ServiceId,
            entry.ServiceCode,
            entry.ServiceName,
            entry.BookingMode,
            entry.VesselId,
            entry.VesselCode,
            entry.VesselName,
            entry.RouteId,
            entry.RouteCode,
            entry.RouteName,
            entry.FromStationId,
            entry.FromStationCode,
            entry.FromLocation,
            entry.ToStationId,
            entry.ToStationCode,
            entry.ToLocation,
            entry.OperatingDate,
            entry.StartAt,
            entry.EndAt,
            entry.Status,
            entry.ScheduleState,
            entry.OperationStatus,
            entry.PaymentStage,
            entry.RemainingPaymentDeadline,
            entry.IsPaymentOverdue,
            entry.DelayMinutes,
            entry.DelayReason,
            entry.AdjustedStartAt,
            entry.AdjustedEndAt,
            entry.ActualStartAt,
            entry.ActualEndAt,
            entry.SyncedAt);
}

internal static class OperationScheduleAccess
{
    public static async Task<IQueryable<OperationScheduleEntry>> ApplyVisibilityAsync(
        IApplicationDbContext context,
        IQueryable<OperationScheduleEntry> query,
        User actor,
        CancellationToken cancellationToken)
    {
        if (AuthSupport.IsAdmin(actor))
        {
            return query;
        }

        if (AuthSupport.IsManager(actor))
        {
            var stationIds = await GetActiveStationIdsAsync(context, actor.Id, cancellationToken);
            return query.Where(x =>
                (x.FromStationId.HasValue && stationIds.Contains(x.FromStationId.Value))
                || (x.ToStationId.HasValue && stationIds.Contains(x.ToStationId.Value))
                || (x.SourceType == OperationScheduleSourceType.CustomBooking
                    && x.AssignedManagerUserId == actor.Id));
        }

        if (AuthSupport.IsStaff(actor))
        {
            var stationIds = await GetActiveStationIdsAsync(context, actor.Id, cancellationToken);
            var assignedCustomBookingIds = context.Set<CustomBookingStaffAssignment>()
                .Where(x => x.StaffUserId == actor.Id)
                .Select(x => x.CustomBookingRequestId);

            return query.Where(x =>
                (x.FromStationId.HasValue && stationIds.Contains(x.FromStationId.Value))
                || (x.ToStationId.HasValue && stationIds.Contains(x.ToStationId.Value))
                || (x.SourceType == OperationScheduleSourceType.CustomBooking
                    && assignedCustomBookingIds.Contains(x.SourceId)));
        }

        if (AuthSupport.IsCustomer(actor))
        {
            return query.Where(x =>
                x.SourceType == OperationScheduleSourceType.RegularTrip
                || (x.SourceType == OperationScheduleSourceType.CustomBooking
                    && x.CustomerUserId == actor.Id));
        }

        throw new ForbiddenAccessException();
    }

    public static async Task<bool> CanManageEntryAsync(
        IApplicationDbContext context,
        OperationScheduleEntry entry,
        User actor,
        CancellationToken cancellationToken)
    {
        if (AuthSupport.IsAdmin(actor))
        {
            return true;
        }

        if (AuthSupport.IsManager(actor))
        {
            if (entry.SourceType == OperationScheduleSourceType.CustomBooking
                && entry.AssignedManagerUserId == actor.Id)
            {
                return true;
            }

            return await IsAssignedToEntryStationAsync(context, actor.Id, entry, cancellationToken);
        }

        if (AuthSupport.IsStaff(actor))
        {
            if (entry.SourceType == OperationScheduleSourceType.CustomBooking
                && await context.Set<CustomBookingStaffAssignment>()
                    .AnyAsync(x =>
                            x.CustomBookingRequestId == entry.SourceId
                            && x.StaffUserId == actor.Id,
                        cancellationToken))
            {
                return true;
            }

            return await IsAssignedToEntryStationAsync(context, actor.Id, entry, cancellationToken);
        }

        return false;
    }

    private static async Task<Guid[]> GetActiveStationIdsAsync(
        IApplicationDbContext context,
        Guid userId,
        CancellationToken cancellationToken) =>
        await context.UserStationAssignments
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive)
            .Select(x => x.StationId)
            .ToArrayAsync(cancellationToken);

    private static async Task<bool> IsAssignedToEntryStationAsync(
        IApplicationDbContext context,
        Guid userId,
        OperationScheduleEntry entry,
        CancellationToken cancellationToken)
    {
        if (!entry.FromStationId.HasValue && !entry.ToStationId.HasValue)
        {
            return false;
        }

        var fromStationId = entry.FromStationId;
        var toStationId = entry.ToStationId;
        return await context.UserStationAssignments
            .AsNoTracking()
            .AnyAsync(x =>
                    x.UserId == userId
                    && x.IsActive
                    && (x.StationId == fromStationId || x.StationId == toStationId),
                cancellationToken);
    }
}

internal static class OperationScheduleStates
{
    public const string Request = "Request";
    public const string TentativeHold = "TentativeHold";
    public const string Reserved = "Reserved";
    public const string ReadyForService = "ReadyForService";
    public const string PaymentOverdue = "PaymentOverdue";
    public const string Cancelled = "Cancelled";
}

internal static class OperationPaymentStages
{
    public const string NotRequired = "NotRequired";
    public const string NotCreated = "NotCreated";
    public const string DepositPending = "DepositPending";
    public const string DepositPaid = "DepositPaid";
    public const string FullyPaid = "FullyPaid";
    public const string Failed = "Failed";
}

internal static class OperationStatuses
{
    public const string Scheduled = "Scheduled";
    public const string Boarding = "Boarding";
    public const string Departed = "Departed";
    public const string Arrived = "Arrived";
    public const string Delayed = "Delayed";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

public sealed class OperationScheduleSynchronizer : IOperationScheduleSynchronizer
{
    private static readonly TimeSpan VietnamUtcOffset = TimeSpan.FromHours(7);

    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly IDatabaseExceptionClassifier _exceptionClassifier;
    private readonly ILogger<OperationScheduleSynchronizer> _logger;

    public OperationScheduleSynchronizer(
        IApplicationDbContext context,
        TimeProvider timeProvider,
        IDatabaseExceptionClassifier exceptionClassifier,
        ILogger<OperationScheduleSynchronizer> logger)
    {
        _context = context;
        _timeProvider = timeProvider;
        _exceptionClassifier = exceptionClassifier;
        _logger = logger;
    }

    public async Task<int> SyncAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var context = _context;
        var now = _timeProvider.GetUtcNow();
        var drafts = new List<OperationScheduleDraft>();
        drafts.AddRange(await BuildRegularTripDraftsAsync(context, from, to, now, cancellationToken));
        drafts.AddRange(await BuildCustomBookingDraftsAsync(context, from, to, now, cancellationToken));

        var regularTripIds = drafts
            .Where(x => x.SourceType == OperationScheduleSourceType.RegularTrip)
            .Select(x => x.SourceId)
            .ToArray();
        var customBookingIds = drafts
            .Where(x => x.SourceType == OperationScheduleSourceType.CustomBooking)
            .Select(x => x.SourceId)
            .ToArray();

        var existingEntries = await context.OperationScheduleEntries
            .Where(x => (x.StartAt < to && x.EndAt > from)
                        || (x.SourceType == OperationScheduleSourceType.RegularTrip
                            && regularTripIds.Contains(x.SourceId))
                        || (x.SourceType == OperationScheduleSourceType.CustomBooking
                            && customBookingIds.Contains(x.SourceId)))
            .ToListAsync(cancellationToken);

        var existingBySource = existingEntries
            .GroupBy(x => (x.SourceType, x.SourceId))
            .ToDictionary(x => x.Key, x => x.First());

        var syncedKeys = new HashSet<(OperationScheduleSourceType SourceType, Guid SourceId)>();
        foreach (var draft in drafts)
        {
            var key = (draft.SourceType, draft.SourceId);
            syncedKeys.Add(key);

            if (!existingBySource.TryGetValue(key, out var entry))
            {
                entry = new OperationScheduleEntry
                {
                    SourceType = draft.SourceType,
                    SourceId = draft.SourceId
                };
                context.OperationScheduleEntries.Add(entry);
            }

            ApplyDraft(entry, draft);
        }

        foreach (var staleEntry in existingEntries
                     .Where(x => x.StartAt < to
                                 && x.EndAt > from
                                 && !syncedKeys.Contains((x.SourceType, x.SourceId))))
        {
            context.OperationScheduleEntries.Remove(staleEntry);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (_exceptionClassifier.IsUniqueConstraintViolation(ex))
        {
            // Một lần đồng bộ song song (job nền + refresh thủ công, hoặc nhiều instance) đã ghi
            // trùng khoá (source_type, source_id). Sync là idempotent nên bỏ qua an toàn; chu kỳ
            // kế tiếp sẽ tự hoà dữ liệu.
            _logger.LogDebug(
                ex,
                "Bỏ qua một lần ghi trùng khoá khi đồng bộ lịch vận hành; sẽ hoà lại ở chu kỳ kế tiếp.");
        }

        return syncedKeys.Count;
    }

    private static async Task<IReadOnlyList<OperationScheduleDraft>> BuildRegularTripDraftsAsync(
        IApplicationDbContext context,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var trips = await context.Set<Trip>()
            .AsNoTracking()
            .Include(x => x.Route)
            .ThenInclude(x => x.RouteStops)
            .ThenInclude(x => x.Station)
            .Where(x => x.DepartureTime < to && x.ArrivalTime > from)
            .ToArrayAsync(cancellationToken);

        return trips
            .Select(trip =>
            {
                var routeStops = trip.Route.RouteStops
                    .OrderBy(x => x.StopOrder)
                    .ToArray();
                var firstStop = routeStops.FirstOrDefault();
                var lastStop = routeStops.LastOrDefault();

                return new OperationScheduleDraft(
                    OperationScheduleSourceType.RegularTrip,
                    trip.Id,
                    trip.TripCode,
                    $"Trip {trip.TripCode}",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    trip.RouteId,
                    trip.Route.RouteCode,
                    trip.Route.RouteName,
                    firstStop?.StationId,
                    firstStop?.Station.StationCode,
                    firstStop?.Station.StationName ?? trip.Route.RouteName,
                    lastStop?.StationId,
                    lastStop?.Station.StationCode,
                    lastStop?.Station.StationName ?? trip.Route.RouteName,
                    trip.OperatingDate,
                    trip.DepartureTime.ToUniversalTime(),
                    trip.ArrivalTime.ToUniversalTime(),
                    MapTripStatusToOperationStatus(trip.TripStatus),
                    trip.TripStatus == TripStatus.Cancelled
                        ? OperationScheduleStates.Cancelled
                        : trip.TripStatus.ToString(),
                    OperationPaymentStages.NotRequired,
                    null,
                    false,
                    null,
                    null,
                    now);
            })
            .ToArray();
    }

    private static async Task<IReadOnlyList<OperationScheduleDraft>> BuildCustomBookingDraftsAsync(
        IApplicationDbContext context,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var fromDate = DateOnly.FromDateTime(from.ToOffset(VietnamUtcOffset).DateTime);
        var toDate = DateOnly.FromDateTime(to.ToOffset(VietnamUtcOffset).DateTime);
        var requests = await context.Set<CustomBookingRequest>()
            .AsNoTracking()
            .Include(x => x.WaterbusService)
            .Include(x => x.AssignedVessel)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.Quote)
            .Where(x => x.PreferredStartTime.HasValue
                        && x.DepartureDate <= toDate
                        && (x.EstimatedEndDate ?? x.DepartureDate) >= fromDate)
            .ToArrayAsync(cancellationToken);

        return requests
            .Select(x => TryCreateCustomBookingDraft(x, now))
            .Where(x => x is not null
                        && x.StartAt < to
                        && x.EndAt > from)
            .Select(x => x!)
            .ToArray();
    }

    private static OperationScheduleDraft? TryCreateCustomBookingDraft(
        CustomBookingRequest request,
        DateTimeOffset now)
    {
        if (!request.PreferredStartTime.HasValue)
        {
            return null;
        }

        var startAt = new DateTimeOffset(
            request.DepartureDate.ToDateTime(request.PreferredStartTime.Value),
            VietnamUtcOffset).ToUniversalTime();
        var endAt = request.EstimatedEndDate.HasValue && request.PreferredEndTime.HasValue
            ? new DateTimeOffset(
                request.EstimatedEndDate.Value.ToDateTime(request.PreferredEndTime.Value),
                VietnamUtcOffset).ToUniversalTime()
            : startAt.AddMinutes(Math.Max(1, request.EstimatedDurationMinutes));

        if (endAt <= startAt)
        {
            endAt = startAt.AddMinutes(Math.Max(1, request.EstimatedDurationMinutes));
        }

        var paymentStage = ResolvePaymentStage(request.Quote);
        var remainingPaymentDeadline = request.Quote?.RemainingAmount > 0
            ? (DateTimeOffset?)startAt.AddHours(-24)
            : null;
        var isPaymentOverdue = remainingPaymentDeadline.HasValue
            && now >= remainingPaymentDeadline.Value
            && !CustomBookingPaymentSupport.IsFullyPaid(request.Quote);

        return new OperationScheduleDraft(
            OperationScheduleSourceType.CustomBooking,
            request.Id,
            CustomBookingPaymentSupport.CreateBookingReference(request),
            request.WaterbusService is null
                ? "Custom booking"
                : $"Custom booking - {request.WaterbusService.Name}",
            request.WaterbusServiceId,
            request.WaterbusService?.Code,
            request.WaterbusService?.Name,
            request.WaterbusService?.BookingMode,
            request.AssignedVesselId,
            request.AssignedVessel?.Code,
            request.AssignedVessel?.Name,
            null,
            null,
            null,
            request.FromStationId,
            request.FromStationCode ?? request.FromStation?.StationCode,
            request.FromStation?.StationName ?? request.FromLocation,
            request.ToStationId,
            request.ToStationCode ?? request.ToStation?.StationCode,
            request.ToStation?.StationName ?? request.ToLocation,
            request.DepartureDate,
            startAt,
            endAt,
            request.Status.ToString(),
            ResolveCustomBookingScheduleState(request, paymentStage, isPaymentOverdue),
            paymentStage,
            remainingPaymentDeadline,
            isPaymentOverdue,
            request.UserId,
            request.AssignedManagerUserId,
            now);
    }

    private static string ResolveCustomBookingScheduleState(
        CustomBookingRequest request,
        string paymentStage,
        bool isPaymentOverdue)
    {
        if (request.Status == CustomBookingRequestStatus.Cancelled)
        {
            return OperationScheduleStates.Cancelled;
        }

        if (request.Status == CustomBookingRequestStatus.PendingReview)
        {
            return OperationScheduleStates.Request;
        }

        if (isPaymentOverdue)
        {
            return OperationScheduleStates.PaymentOverdue;
        }

        if (paymentStage == OperationPaymentStages.FullyPaid)
        {
            return OperationScheduleStates.ReadyForService;
        }

        if (request.Status == CustomBookingRequestStatus.Confirmed)
        {
            return OperationScheduleStates.Reserved;
        }

        return request.AssignedVesselId.HasValue
            ? OperationScheduleStates.TentativeHold
            : OperationScheduleStates.Request;
    }

    private static string ResolvePaymentStage(CustomBookingQuote? quote)
    {
        if (quote is null)
        {
            return OperationPaymentStages.NotCreated;
        }

        if (CustomBookingPaymentSupport.IsFullyPaid(quote))
        {
            return OperationPaymentStages.FullyPaid;
        }

        if (quote.DepositPaymentStatus == CustomBookingDepositPaymentStatus.Paid)
        {
            return OperationPaymentStages.DepositPaid;
        }

        if (quote.DepositPaymentStatus == CustomBookingDepositPaymentStatus.Pending)
        {
            return OperationPaymentStages.DepositPending;
        }

        if (quote.DepositPaymentStatus == CustomBookingDepositPaymentStatus.Failed
            || quote.RemainingPaymentStatus == CustomBookingDepositPaymentStatus.Failed)
        {
            return OperationPaymentStages.Failed;
        }

        return OperationPaymentStages.NotCreated;
    }

    private static void ApplyDraft(OperationScheduleEntry entry, OperationScheduleDraft draft)
    {
        entry.SourceCode = draft.SourceCode;
        entry.Title = draft.Title;
        entry.ServiceId = draft.ServiceId;
        entry.ServiceCode = draft.ServiceCode;
        entry.ServiceName = draft.ServiceName;
        entry.BookingMode = draft.BookingMode;
        entry.VesselId = draft.VesselId;
        entry.VesselCode = draft.VesselCode;
        entry.VesselName = draft.VesselName;
        entry.RouteId = draft.RouteId;
        entry.RouteCode = draft.RouteCode;
        entry.RouteName = draft.RouteName;
        entry.FromStationId = draft.FromStationId;
        entry.FromStationCode = draft.FromStationCode;
        entry.FromLocation = draft.FromLocation;
        entry.ToStationId = draft.ToStationId;
        entry.ToStationCode = draft.ToStationCode;
        entry.ToLocation = draft.ToLocation;
        entry.OperatingDate = draft.OperatingDate;
        entry.StartAt = draft.StartAt;
        entry.EndAt = draft.EndAt;
        if (entry.DelayMinutes > 0)
        {
            entry.AdjustedStartAt = entry.StartAt.AddMinutes(entry.DelayMinutes);
            entry.AdjustedEndAt = entry.EndAt.AddMinutes(entry.DelayMinutes);
        }

        entry.Status = draft.Status;
        entry.ScheduleState = draft.ScheduleState;
        if (draft.ScheduleState == OperationScheduleStates.Cancelled)
        {
            entry.OperationStatus = OperationStatuses.Cancelled;
        }
        else if (string.Equals(entry.OperationStatus, OperationStatuses.Delayed, StringComparison.Ordinal)
                 && IsProgressedPastDelay(draft))
        {
            entry.OperationStatus = ResolveDefaultOperationStatus(draft);
        }
        else if (string.IsNullOrWhiteSpace(entry.OperationStatus)
                 || IsAutoSyncedOperationStatus(entry.OperationStatus))
        {
            entry.OperationStatus = ResolveDefaultOperationStatus(draft);
        }

        entry.PaymentStage = draft.PaymentStage;
        entry.RemainingPaymentDeadline = draft.RemainingPaymentDeadline;
        entry.IsPaymentOverdue = draft.IsPaymentOverdue;
        entry.CustomerUserId = draft.CustomerUserId;
        entry.AssignedManagerUserId = draft.AssignedManagerUserId;
        entry.SyncedAt = draft.SyncedAt;
    }

    private static bool IsAutoSyncedOperationStatus(string operationStatus) =>
        string.Equals(operationStatus, OperationStatuses.Scheduled, StringComparison.Ordinal)
        || string.Equals(operationStatus, OperationStatuses.Boarding, StringComparison.Ordinal)
        || string.Equals(operationStatus, OperationStatuses.Departed, StringComparison.Ordinal)
        || string.Equals(operationStatus, OperationStatuses.Arrived, StringComparison.Ordinal)
        || string.Equals(operationStatus, OperationStatuses.Cancelled, StringComparison.Ordinal);

    private static bool IsProgressedPastDelay(OperationScheduleDraft draft) =>
        draft.SourceType == OperationScheduleSourceType.RegularTrip
        && (string.Equals(draft.Status, OperationStatuses.Boarding, StringComparison.Ordinal)
            || string.Equals(draft.Status, OperationStatuses.Departed, StringComparison.Ordinal)
            || string.Equals(draft.Status, OperationStatuses.Arrived, StringComparison.Ordinal)
            || string.Equals(draft.Status, OperationStatuses.Cancelled, StringComparison.Ordinal));

    private static string ResolveDefaultOperationStatus(OperationScheduleDraft draft)
    {
        if (draft.SourceType == OperationScheduleSourceType.RegularTrip
            && (string.Equals(draft.Status, OperationStatuses.Boarding, StringComparison.Ordinal)
                || string.Equals(draft.Status, OperationStatuses.Departed, StringComparison.Ordinal)
                || string.Equals(draft.Status, OperationStatuses.Arrived, StringComparison.Ordinal)
                || string.Equals(draft.Status, OperationStatuses.Delayed, StringComparison.Ordinal)
                || string.Equals(draft.Status, OperationStatuses.Cancelled, StringComparison.Ordinal)))
        {
            return draft.Status;
        }

        return draft.ScheduleState == OperationScheduleStates.Cancelled
            ? OperationStatuses.Cancelled
            : OperationStatuses.Scheduled;
    }

    private static string MapTripStatusToOperationStatus(TripStatus status) =>
        status switch
        {
            TripStatus.Boarding => OperationStatuses.Boarding,
            TripStatus.InProgress => OperationStatuses.Departed,
            TripStatus.Completed => OperationStatuses.Arrived,
            TripStatus.Cancelled => OperationStatuses.Cancelled,
            TripStatus.Delayed => OperationStatuses.Delayed,
            _ => OperationStatuses.Scheduled
        };

    private sealed record OperationScheduleDraft(
        OperationScheduleSourceType SourceType,
        Guid SourceId,
        string SourceCode,
        string Title,
        Guid? ServiceId,
        string? ServiceCode,
        string? ServiceName,
        BookingMode? BookingMode,
        Guid? VesselId,
        string? VesselCode,
        string? VesselName,
        Guid? RouteId,
        string? RouteCode,
        string? RouteName,
        Guid? FromStationId,
        string? FromStationCode,
        string FromLocation,
        Guid? ToStationId,
        string? ToStationCode,
        string ToLocation,
        DateOnly OperatingDate,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        string Status,
        string ScheduleState,
        string? PaymentStage,
        DateTimeOffset? RemainingPaymentDeadline,
        bool IsPaymentOverdue,
        Guid? CustomerUserId,
        Guid? AssignedManagerUserId,
        DateTimeOffset SyncedAt);
}
