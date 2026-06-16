using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record CancelCustomBookingRequestCommand(
    Guid Id,
    string Reason) : IRequest<CustomBookingRequestDto>;

public sealed class CancelCustomBookingRequestCommandValidator
    : AbstractValidator<CancelCustomBookingRequestCommand>
{
    public CancelCustomBookingRequestCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id yêu cầu thuê tàu không hợp lệ.");
        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Lý do hủy là bắt buộc.")
            .MaximumLength(500)
            .WithMessage("Lý do hủy không được vượt quá 500 ký tự.");
    }
}

public sealed class CancelCustomBookingRequestCommandHandler
    : IRequestHandler<CancelCustomBookingRequestCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public CancelCustomBookingRequestCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<CustomBookingRequestDto> Handle(
        CancelCustomBookingRequestCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var canManage = AuthSupport.IsAdmin(actor);
        var isOwner = AuthSupport.IsCustomer(actor) && actor.Id == await _context.Set<CustomBookingRequest>()
            .Where(x => x.Id == request.Id)
            .Select(x => x.UserId)
            .SingleOrDefaultAsync(cancellationToken);

        if (!canManage && !isOwner)
        {
            throw new ForbiddenAccessException();
        }

        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        CustomBookingRequestSupport.EnsureCanCancel(customRequest);

        customRequest.Status = CustomBookingRequestStatus.Cancelled;
        customRequest.StatusReason = request.Reason.Trim();
        customRequest.CancelledAt = _timeProvider.GetUtcNow();
        customRequest.CancelledByUserId = actor.Id;

        await _context.SaveChangesAsync(cancellationToken);

        var routeSegments = await CustomBookingRequestSupport.GetMatchingRouteSegmentsAsync(
            _context,
            customRequest,
            cancellationToken);

        return CustomBookingRequestDto.From(customRequest, routeSegments);
    }
}
