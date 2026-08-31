using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Users;

public sealed record DeleteUserRequest(Guid UserId);

public sealed class DeleteUserRequestValidator : AbstractValidator<DeleteUserRequest>
{
    public DeleteUserRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId không hợp lệ.");
    }
}

public sealed class DeleteUserRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public DeleteUserRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<AuthActionResultDto> ExecuteAsync(DeleteUserRequest request, CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.EnsureCurrentUserCanManageUsersAsync(_context, _userContext, cancellationToken);
        var user = await _context.Set<User>()
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == request.UserId, cancellationToken)
            ?? throw new global::SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy user.");

        UserManagementSupport.EnsureCanDeleteUser(actor, user);

        var now = _timeProvider.GetUtcNow();
        if (AuthSupport.IsStaff(user))
        {
            await EnsureStaffHasNoOperationalAssignmentAsync(user.Id, now, cancellationToken);
        }

        await AuthSupport.RevokeActiveRefreshTokensAsync(_context, user.Id, now, cancellationToken);
        user.Status = UserStatus.Deleted;
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthActionResultDto("Xoa user thanh cong.");
    }

    private async Task EnsureStaffHasNoOperationalAssignmentAsync(
        Guid staffUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var usableAssignments = await _context.StaffWorkAssignments
            .AsNoTracking()
            .Where(x => x.StaffUserId == staffUserId
                && x.Status != StaffWorkAssignmentStatus.Cancelled
                && x.Status != StaffWorkAssignmentStatus.Replaced)
            .Select(x => new
            {
                x.AssignmentType,
                x.BoatId,
                x.StartAt,
                x.EndAt
            })
            .ToListAsync(cancellationToken);

        if (usableAssignments.Any(x => x.EndAt >= now))
        {
            throw AuthSupport.CreateValidationException(
                nameof(DeleteUserRequest.UserId),
                "Staff còn ca làm hiện tại hoặc sắp tới. Hãy thay thế/hủy toàn bộ ca trước khi xóa tài khoản.");
        }

        var endedBoatAssignments = usableAssignments
            .Where(x => x.AssignmentType == StaffWorkAssignmentType.Boat && x.BoatId.HasValue)
            .ToList();
        if (endedBoatAssignments.Count == 0)
        {
            return;
        }

        var boatIds = endedBoatAssignments.Select(x => x.BoatId!.Value).Distinct().ToArray();
        var unfinishedTrips = await _context.Set<Trip>()
            .AsNoTracking()
            .Where(x => x.BoatId.HasValue
                && boatIds.Contains(x.BoatId.Value)
                && x.TripStatus != TripStatus.Completed
                && x.TripStatus != TripStatus.Cancelled)
            .Select(x => new { x.BoatId, x.DepartureTime, x.ArrivalTime })
            .ToListAsync(cancellationToken);
        var stillCoversUnfinishedTrip = endedBoatAssignments.Any(assignment => unfinishedTrips.Any(trip =>
            trip.BoatId == assignment.BoatId
            && assignment.StartAt <= trip.DepartureTime
            && assignment.EndAt >= trip.ArrivalTime));
        if (stillCoversUnfinishedTrip)
        {
            throw AuthSupport.CreateValidationException(
                nameof(DeleteUserRequest.UserId),
                "Staff vẫn thuộc tổ OnBoard của chuyến chưa hoàn tất. Hãy phân công người thay thế và kết thúc/hủy ca trước khi xóa tài khoản.");
        }
    }
}
