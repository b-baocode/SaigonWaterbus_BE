using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record CustomBookingManagerCandidateDto(
    Guid UserId,
    string FullName,
    string? PhoneNumber,
    string? Email,
    Guid StationId,
    string StationCode,
    string StationName,
    bool IsPrimaryStation);

public sealed record GetCustomBookingManagerCandidatesQuery(Guid Id)
    : IRequest<IReadOnlyCollection<CustomBookingManagerCandidateDto>>;

public sealed class GetCustomBookingManagerCandidatesQueryValidator
    : AbstractValidator<GetCustomBookingManagerCandidatesQuery>
{
    public GetCustomBookingManagerCandidatesQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Id yêu cầu thuê tàu không hợp lệ.");
    }
}

public sealed class GetCustomBookingManagerCandidatesQueryHandler
    : IRequestHandler<GetCustomBookingManagerCandidatesQuery, IReadOnlyCollection<CustomBookingManagerCandidateDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCustomBookingManagerCandidatesQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyCollection<CustomBookingManagerCandidateDto>> Handle(
        GetCustomBookingManagerCandidatesQuery request,
        CancellationToken cancellationToken)
    {
        await CustomBookingRequestSupport.EnsureCurrentUserCanManageCustomBookingRequestsAsync(
            _context,
            _userContext,
            cancellationToken);

        var customRequest = await _context.Set<CustomBookingRequest>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        CustomBookingRequestSupport.EnsureCanAssignManager(customRequest);
        var stationId = customRequest.FromStationId!.Value;

        return await _context.Set<UserStationAssignment>()
            .AsNoTracking()
            .Where(x =>
                x.StationId == stationId
                && x.IsActive
                && x.User.Status == UserStatus.Active
                && x.User.Role.SystemName == Roles.ManagerSystemName)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.User.FullName)
            .Select(x => new CustomBookingManagerCandidateDto(
                x.UserId,
                x.User.FullName,
                x.User.PhoneNumber,
                x.User.Email,
                x.StationId,
                x.Station.StationCode,
                x.Station.StationName,
                x.IsPrimary))
            .ToArrayAsync(cancellationToken);
    }
}

public sealed record AssignCustomBookingManagerCommand(Guid Id, Guid ManagerUserId)
    : IRequest<CustomBookingRequestDto>;

public sealed class AssignCustomBookingManagerCommandValidator
    : AbstractValidator<AssignCustomBookingManagerCommand>
{
    public AssignCustomBookingManagerCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Id yêu cầu thuê tàu không hợp lệ.");
        RuleFor(x => x.ManagerUserId).NotEmpty().WithMessage("ManagerUserId là bắt buộc.");
    }
}

public sealed class AssignCustomBookingManagerCommandHandler
    : IRequestHandler<AssignCustomBookingManagerCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public AssignCustomBookingManagerCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<CustomBookingRequestDto> Handle(
        AssignCustomBookingManagerCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await CustomBookingRequestSupport.EnsureCurrentUserCanManageCustomBookingRequestsAsync(
            _context,
            _userContext,
            cancellationToken);
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        CustomBookingRequestSupport.EnsureCanAssignManager(customRequest);
        var stationId = customRequest.FromStationId!.Value;
        var manager = await _context.Set<User>()
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == request.ManagerUserId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy Manager.");

        if (!AuthSupport.IsManager(manager) || manager.Status != UserStatus.Active)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.ManagerUserId),
                "Người được chọn phải là Manager đang hoạt động.");
        }

        var managesDepartureStation = await _context.Set<UserStationAssignment>()
            .AnyAsync(x =>
                x.UserId == manager.Id
                && x.StationId == stationId
                && x.IsActive,
                cancellationToken);
        if (!managesDepartureStation)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.ManagerUserId),
                "Manager được chọn không phụ trách bến khởi hành.");
        }

        if (customRequest.AssignedManagerUserId != manager.Id)
        {
            _context.Set<CustomBookingStaffAssignment>().RemoveRange(customRequest.StaffAssignments);
            _context.Set<CustomBookingOperationService>().RemoveRange(customRequest.OperationServices);
            customRequest.StaffAssignments.Clear();
            customRequest.OperationServices.Clear();
        }

        customRequest.AssignedManagerUserId = manager.Id;
        customRequest.AssignedManagerUser = manager;
        customRequest.ManagerAssignedAt = _timeProvider.GetUtcNow();
        customRequest.ManagerAssignedByUserId = actor.Id;
        await _context.SaveChangesAsync(cancellationToken);

        var routeSegments = await CustomBookingRequestSupport.GetMatchingRouteSegmentsAsync(
            _context,
            customRequest,
            cancellationToken);
        return CustomBookingRequestDto.From(customRequest, routeSegments);
    }
}

public sealed record CustomBookingStaffCandidateDto(
    Guid UserId,
    string FullName,
    string? PhoneNumber,
    string? Email,
    bool IsPrimaryStation);

public sealed record GetCustomBookingStaffCandidatesQuery(Guid Id)
    : IRequest<IReadOnlyCollection<CustomBookingStaffCandidateDto>>;

public sealed class GetCustomBookingStaffCandidatesQueryValidator
    : AbstractValidator<GetCustomBookingStaffCandidatesQuery>
{
    public GetCustomBookingStaffCandidatesQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Id yêu cầu thuê tàu không hợp lệ.");
    }
}

public sealed class GetCustomBookingStaffCandidatesQueryHandler
    : IRequestHandler<GetCustomBookingStaffCandidatesQuery, IReadOnlyCollection<CustomBookingStaffCandidateDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCustomBookingStaffCandidatesQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyCollection<CustomBookingStaffCandidateDto>> Handle(
        GetCustomBookingStaffCandidatesQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        CustomBookingRequestSupport.EnsureAssignedManagerCanPlanOperations(customRequest, actor);
        var stationId = customRequest.FromStationId!.Value;

        return await _context.Set<UserStationAssignment>()
            .AsNoTracking()
            .Where(x =>
                x.StationId == stationId
                && x.IsActive
                && x.User.Status == UserStatus.Active
                && x.User.Role.SystemName == Roles.StaffSystemName)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.User.FullName)
            .Select(x => new CustomBookingStaffCandidateDto(
                x.UserId,
                x.User.FullName,
                x.User.PhoneNumber,
                x.User.Email,
                x.IsPrimary))
            .ToArrayAsync(cancellationToken);
    }
}

public sealed record CustomBookingStaffPlanItem(Guid StaffUserId, string? DutyNote = null);

public sealed record CustomBookingOperationServicePlanItem(
    string ServiceName,
    int Quantity,
    string? Note = null);

public sealed record UpdateCustomBookingOperationPlanCommand(
    Guid Id,
    IReadOnlyCollection<CustomBookingStaffPlanItem> StaffAssignments,
    IReadOnlyCollection<CustomBookingOperationServicePlanItem> Services)
    : IRequest<CustomBookingRequestDto>;

public sealed class UpdateCustomBookingOperationPlanCommandValidator
    : AbstractValidator<UpdateCustomBookingOperationPlanCommand>
{
    public UpdateCustomBookingOperationPlanCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Id yêu cầu thuê tàu không hợp lệ.");
        RuleFor(x => x.StaffAssignments)
            .NotNull()
            .WithMessage("Danh sách Staff là bắt buộc.");
        RuleFor(x => x.StaffAssignments)
            .Must(x => x.Count <= 50)
            .WithMessage("Mỗi booking chỉ được phân tối đa 50 Staff.")
            .When(x => x.StaffAssignments is not null);
        RuleFor(x => x.StaffAssignments)
            .Must(x => x.Select(item => item.StaffUserId).Distinct().Count() == x.Count)
            .WithMessage("Danh sách Staff không được trùng.")
            .When(x => x.StaffAssignments is not null);
        RuleForEach(x => x.StaffAssignments).ChildRules(item =>
        {
            item.RuleFor(x => x.StaffUserId).NotEmpty().WithMessage("StaffUserId không hợp lệ.");
            item.RuleFor(x => x.DutyNote).MaximumLength(500);
        });

        RuleFor(x => x.Services)
            .NotNull()
            .WithMessage("Danh sách dịch vụ vận hành là bắt buộc.");
        RuleFor(x => x.Services)
            .Must(x => x.Count <= 30)
            .WithMessage("Mỗi booking chỉ được khai báo tối đa 30 dịch vụ vận hành.")
            .When(x => x.Services is not null);
        RuleFor(x => x.Services)
            .Must(x => x
                .Select(item => item.ServiceName?.Trim() ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == x.Count)
            .WithMessage("Tên dịch vụ vận hành không được trùng.")
            .When(x => x.Services is not null);
        RuleForEach(x => x.Services).ChildRules(item =>
        {
            item.RuleFor(x => x.ServiceName).NotEmpty().MaximumLength(150);
            item.RuleFor(x => x.Quantity).InclusiveBetween(1, 1000);
            item.RuleFor(x => x.Note).MaximumLength(500);
        });
    }
}

public sealed class UpdateCustomBookingOperationPlanCommandHandler
    : IRequestHandler<UpdateCustomBookingOperationPlanCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public UpdateCustomBookingOperationPlanCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<CustomBookingRequestDto> Handle(
        UpdateCustomBookingOperationPlanCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        CustomBookingRequestSupport.EnsureAssignedManagerCanPlanOperations(customRequest, actor);
        var staffIds = request.StaffAssignments.Select(x => x.StaffUserId).ToArray();
        var validStaffIds = await _context.Set<UserStationAssignment>()
            .Where(x =>
                staffIds.Contains(x.UserId)
                && x.StationId == customRequest.FromStationId
                && x.IsActive
                && x.User.Status == UserStatus.Active
                && x.User.Role.SystemName == Roles.StaffSystemName)
            .Select(x => x.UserId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        var invalidStaffIds = staffIds.Except(validStaffIds).ToArray();
        if (invalidStaffIds.Length > 0)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.StaffAssignments),
                "Một hoặc nhiều Staff không hoạt động hoặc không được gắn với bến khởi hành.");
        }

        _context.Set<CustomBookingStaffAssignment>().RemoveRange(customRequest.StaffAssignments);
        _context.Set<CustomBookingOperationService>().RemoveRange(customRequest.OperationServices);
        var now = _timeProvider.GetUtcNow();

        var staffAssignments = request.StaffAssignments
            .Select(item => new CustomBookingStaffAssignment
            {
                CustomBookingRequestId = customRequest.Id,
                StaffUserId = item.StaffUserId,
                DutyNote = string.IsNullOrWhiteSpace(item.DutyNote) ? null : item.DutyNote.Trim(),
                AssignedAt = now,
                AssignedByManagerUserId = actor.Id
            })
            .ToList();
        var operationServices = request.Services
            .Select(item => new CustomBookingOperationService
            {
                CustomBookingRequestId = customRequest.Id,
                ServiceName = item.ServiceName.Trim(),
                Quantity = item.Quantity,
                Note = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim()
            })
            .ToList();
        _context.Set<CustomBookingStaffAssignment>().AddRange(staffAssignments);
        _context.Set<CustomBookingOperationService>().AddRange(operationServices);

        await _context.SaveChangesAsync(cancellationToken);
        customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleAsync(x => x.Id == request.Id, cancellationToken);
        var routeSegments = await CustomBookingRequestSupport.GetMatchingRouteSegmentsAsync(
            _context,
            customRequest,
            cancellationToken);
        return CustomBookingRequestDto.From(customRequest, routeSegments);
    }
}
