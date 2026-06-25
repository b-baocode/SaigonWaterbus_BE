using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Operations;

public sealed record OperationScheduleItemDto(
    Guid Id,
    string SourceType,
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

        await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);

        return Array.Empty<OperationScheduleItemDto>();
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
        await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy lịch vận hành.");
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
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OperationScheduleSynchronizer> _logger;

    public OperationScheduleSynchronizer(
        IApplicationDbContext context,
        TimeProvider timeProvider,
        ILogger<OperationScheduleSynchronizer> logger)
    {
        _context = context;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<int> SyncAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("OperationScheduleSynchronizer.SyncAsync called but OperationScheduleEntry has been removed.");
        await Task.CompletedTask;
        return 0;
    }
}
