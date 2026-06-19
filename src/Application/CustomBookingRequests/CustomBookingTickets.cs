using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record CustomBookingTicketDto(
    Guid Id,
    Guid CustomBookingRequestId,
    string TicketCode,
    CustomBookingTicketStatus Status,
    string? QrPayload,
    DateTimeOffset QrIssuedAt,
    DateTimeOffset? QrExpiresAt,
    DateTimeOffset? QrUsedAt);

public sealed record CustomBookingTicketQrDto(
    Guid Id,
    Guid CustomBookingRequestId,
    string TicketCode,
    CustomBookingTicketStatus Status,
    string QrToken,
    string QrPayload,
    DateTimeOffset QrIssuedAt,
    DateTimeOffset? QrExpiresAt,
    DateTimeOffset? QrUsedAt);

public sealed record ScanCustomBookingTicketRequest(string? QrToken)
    : IRequest<ScanCustomBookingTicketResultDto>;

public sealed record ScanCustomBookingTicketResultDto(
    Guid TicketId,
    Guid CustomBookingRequestId,
    string TicketCode,
    CustomBookingTicketStatus Status,
    DateTimeOffset? QrUsedAt,
    string Message);

public sealed class ScanCustomBookingTicketRequestValidator : AbstractValidator<ScanCustomBookingTicketRequest>
{
    public ScanCustomBookingTicketRequestValidator()
    {
        RuleFor(x => x.QrToken)
            .NotEmpty()
            .WithMessage("Mã QR là bắt buộc.");
    }
}

internal static class CustomBookingTicketSupport
{
    private const int QrTokenBytes = 32;
    private const string QrPayloadPrefix = "swb:custom-booking:";
    private static readonly TimeSpan VietnamUtcOffset = TimeSpan.FromHours(7);
    private static readonly TimeSpan CheckInLeadTime = TimeSpan.FromMinutes(30);

    public static async Task<(CustomBookingTicket Ticket, string? QrToken)> EnsureActiveTicketAsync(
        IApplicationDbContext context,
        CustomBookingRequest customRequest,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existingTicket = await context.Set<CustomBookingTicket>()
            .Where(x => x.CustomBookingRequestId == customRequest.Id
                     && x.Status == CustomBookingTicketStatus.Active)
            .OrderByDescending(x => x.QrIssuedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingTicket is not null)
        {
            return (existingTicket, existingTicket.QrToken);
        }

        var qrToken = GenerateQrToken();
        var ticket = new CustomBookingTicket
        {
            CustomBookingRequestId = customRequest.Id,
            CustomBookingRequest = customRequest,
            TicketCode = CreateTicketCode(customRequest, now),
            QrTokenHash = HashQrToken(qrToken),
            QrToken = qrToken,
            QrIssuedAt = now,
            QrExpiresAt = customRequest.EstimatedEndDate.HasValue && customRequest.PreferredEndTime.HasValue
                ? new DateTimeOffset(
                    customRequest.EstimatedEndDate.Value.ToDateTime(customRequest.PreferredEndTime.Value),
                    TimeSpan.FromHours(7)).ToUniversalTime()
                : null,
            Status = CustomBookingTicketStatus.Active
        };

        context.Set<CustomBookingTicket>().Add(ticket);
        return (ticket, qrToken);
    }

    public static string CreateQrPayload(string qrToken) => $"{QrPayloadPrefix}{qrToken}";

    public static string HashQrToken(string qrToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(qrToken));
        return Convert.ToHexString(hash);
    }

    public static string ExtractQrToken(string qrTokenOrPayload)
    {
        var trimmed = qrTokenOrPayload.Trim();
        return trimmed.StartsWith(QrPayloadPrefix, StringComparison.Ordinal)
            ? trimmed[QrPayloadPrefix.Length..]
            : trimmed;
    }

    public static DateTimeOffset? CalculateCheckInOpensAt(CustomBookingRequest customRequest)
    {
        if (!customRequest.PreferredStartTime.HasValue)
        {
            return null;
        }

        return new DateTimeOffset(
                customRequest.DepartureDate.ToDateTime(customRequest.PreferredStartTime.Value),
                VietnamUtcOffset)
            .ToUniversalTime()
            .Add(-CheckInLeadTime);
    }

    public static CustomBookingTicketDto CreateDto(
        CustomBookingTicket ticket,
        CustomBookingRequest customRequest,
        User actor)
    {
        var canReceiveQrPayload = AuthSupport.IsAdmin(actor)
            || (AuthSupport.IsManager(actor) && customRequest.AssignedManagerUserId == actor.Id)
            || (AuthSupport.IsCustomer(actor) && customRequest.UserId == actor.Id);

        return new CustomBookingTicketDto(
            ticket.Id,
            ticket.CustomBookingRequestId,
            ticket.TicketCode,
            ticket.Status,
            canReceiveQrPayload
                && CustomBookingPaymentSupport.IsFullyPaid(customRequest.Quote)
                && !string.IsNullOrWhiteSpace(ticket.QrToken)
                ? CreateQrPayload(ticket.QrToken)
                : null,
            ticket.QrIssuedAt,
            ticket.QrExpiresAt,
            ticket.QrUsedAt);
    }

    public static string GenerateQrToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(QrTokenBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string CreateTicketCode(CustomBookingRequest customRequest, DateTimeOffset now) =>
        $"CBT-{now:yyyyMMdd}-{customRequest.Id.ToString("N")[..8].ToUpperInvariant()}";
}

public sealed record GetCustomBookingTicketQuery(Guid Id) : IRequest<CustomBookingTicketDto>;

public sealed record ReissueCustomBookingTicketCommand(Guid Id, string? Reason)
    : IRequest<CustomBookingTicketQrDto>;

public sealed class GetCustomBookingTicketQueryHandler
    : IRequestHandler<GetCustomBookingTicketQuery, CustomBookingTicketDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCustomBookingTicketQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CustomBookingTicketDto> Handle(
        GetCustomBookingTicketQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .Include(x => x.Tickets)
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        CustomBookingRequestSupport.EnsureCanView(customRequest, actor);

        var ticket = customRequest.Tickets
            .Where(x => x.Status == CustomBookingTicketStatus.Active)
            .OrderByDescending(x => x.QrIssuedAt)
            .FirstOrDefault()
            ?? throw AuthSupport.CreateValidationException(nameof(request.Id), "Yêu cầu thuê tàu chưa có vé QR.");

        return CustomBookingTicketSupport.CreateDto(ticket, customRequest, actor);
    }
}

public sealed class ScanCustomBookingTicketRequestHandler
    : IRequestHandler<ScanCustomBookingTicketRequest, ScanCustomBookingTicketResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public ScanCustomBookingTicketRequestHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<ScanCustomBookingTicketResultDto> Handle(
        ScanCustomBookingTicketRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.QrToken))
        {
            throw AuthSupport.CreateValidationException(nameof(request.QrToken), "Mã QR là bắt buộc.");
        }

        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(actor) && !AuthSupport.IsManager(actor) && !AuthSupport.IsStaff(actor))
        {
            throw new ForbiddenAccessException();
        }

        var qrToken = CustomBookingTicketSupport.ExtractQrToken(request.QrToken!);
        var qrTokenHash = CustomBookingTicketSupport.HashQrToken(qrToken);
        var ticket = await _context.Set<CustomBookingTicket>()
            .Include(x => x.CustomBookingRequest)
            .ThenInclude(x => x.Quote)
            .SingleOrDefaultAsync(x => x.QrTokenHash == qrTokenHash, cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(request.QrToken), "Mã QR không hợp lệ.");

        var now = _timeProvider.GetUtcNow();
        if (ticket.QrUsedAt.HasValue || ticket.Status == CustomBookingTicketStatus.Used)
        {
            throw AuthSupport.CreateValidationException(nameof(request.QrToken), "Vé này đã được sử dụng.");
        }

        if (ticket.Status != CustomBookingTicketStatus.Active)
        {
            throw AuthSupport.CreateValidationException(nameof(request.QrToken), "Vé không còn hiệu lực.");
        }

        if (ticket.CustomBookingRequest.Status != CustomBookingRequestStatus.Confirmed)
        {
            throw AuthSupport.CreateValidationException(nameof(request.QrToken), "Vé không còn hiệu lực.");
        }

        if (!CustomBookingPaymentSupport.IsFullyPaid(ticket.CustomBookingRequest.Quote))
        {
            throw AuthSupport.CreateValidationException(nameof(request.QrToken), "Booking chưa thanh toán đủ.");
        }

        if (ticket.CustomBookingRequest.PassengerManifestStatus != PassengerManifestStatus.Completed)
        {
            throw AuthSupport.CreateValidationException(nameof(request.QrToken), "Danh sách hành khách chưa hoàn tất.");
        }

        var checkInOpensAt = CustomBookingTicketSupport.CalculateCheckInOpensAt(ticket.CustomBookingRequest);
        if (checkInOpensAt.HasValue && now < checkInOpensAt.Value)
        {
            throw AuthSupport.CreateValidationException(nameof(request.QrToken), "Chưa đến thời gian check-in.");
        }

        if (ticket.QrExpiresAt.HasValue && ticket.QrExpiresAt <= now)
        {
            ticket.Status = CustomBookingTicketStatus.Expired;
            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException(nameof(request.QrToken), "Vé đã hết hạn.");
        }

        ticket.Status = CustomBookingTicketStatus.Used;
        ticket.QrUsedAt = now;
        ticket.QrUsedByUserId = actor.Id;
        ticket.CustomBookingRequest.PassengerManifestStatus = PassengerManifestStatus.Locked;
        await _context.SaveChangesAsync(cancellationToken);

        return new ScanCustomBookingTicketResultDto(
            ticket.Id,
            ticket.CustomBookingRequestId,
            ticket.TicketCode,
            ticket.Status,
            ticket.QrUsedAt,
            "Check-in vé thành công.");
    }
}

public sealed class ReissueCustomBookingTicketCommandHandler
    : IRequestHandler<ReissueCustomBookingTicketCommand, CustomBookingTicketQrDto>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public ReissueCustomBookingTicketCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<CustomBookingTicketQrDto> Handle(
        ReissueCustomBookingTicketCommand request,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Reason), "Lý do cấp lại QR là bắt buộc.");
        }

        if (reason.Length > 500)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Reason), "Lý do cấp lại QR tối đa 500 ký tự.");
        }

        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .Include(x => x.Tickets)
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        var canReissue = AuthSupport.IsAdmin(actor)
            || (AuthSupport.IsManager(actor) && customRequest.AssignedManagerUserId == actor.Id);
        if (!canReissue)
        {
            throw new ForbiddenAccessException();
        }

        if (customRequest.Status != CustomBookingRequestStatus.Confirmed)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Id), "Chỉ cấp lại QR cho yêu cầu đã xác nhận.");
        }

        if (!CustomBookingPaymentSupport.IsFullyPaid(customRequest.Quote))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Id), "Chỉ cấp lại QR sau khi booking đã thanh toán đủ.");
        }

        var ticket = customRequest.Tickets
            .Where(x => x.Status == CustomBookingTicketStatus.Active)
            .OrderByDescending(x => x.QrIssuedAt)
            .FirstOrDefault()
            ?? throw AuthSupport.CreateValidationException(nameof(request.Id), "Yêu cầu thuê tàu chưa có vé QR active.");

        if (ticket.QrUsedAt.HasValue)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Id), "Vé này đã được sử dụng.");
        }

        var now = _timeProvider.GetUtcNow();
        if (ticket.QrExpiresAt.HasValue && ticket.QrExpiresAt <= now)
        {
            ticket.Status = CustomBookingTicketStatus.Expired;
            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException(nameof(request.Id), "Vé đã hết hạn.");
        }

        var oldValues = new
        {
            ticket.QrTokenHash,
            ticket.QrIssuedAt
        };

        var qrToken = CustomBookingTicketSupport.GenerateQrToken();
        ticket.QrToken = qrToken;
        ticket.QrTokenHash = CustomBookingTicketSupport.HashQrToken(qrToken);
        ticket.QrIssuedAt = now;

        _context.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor.Id,
            Action = "CustomBookingTicketQrReissued",
            TargetTable = "custom_booking_tickets",
            TargetId = ticket.Id,
            OldValues = JsonSerializer.Serialize(oldValues, JsonOptions),
            NewValues = JsonSerializer.Serialize(new
            {
                ticket.QrTokenHash,
                ticket.QrIssuedAt,
                Reason = reason,
                ticket.CustomBookingRequestId
            }, JsonOptions),
            CreatedAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);

        return new CustomBookingTicketQrDto(
            ticket.Id,
            ticket.CustomBookingRequestId,
            ticket.TicketCode,
            ticket.Status,
            qrToken,
            CustomBookingTicketSupport.CreateQrPayload(qrToken),
            ticket.QrIssuedAt,
            ticket.QrExpiresAt,
            ticket.QrUsedAt);
    }
}
