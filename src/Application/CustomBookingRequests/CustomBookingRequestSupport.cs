using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CustomBookingRequests;

internal static class CustomBookingRequestSupport
{
    private static readonly TimeSpan VietnamUtcOffset = TimeSpan.FromHours(7);
    private const int QuoteValidityHours = 24;

    public static IQueryable<CustomBookingRequest> IncludeDetails(IQueryable<CustomBookingRequest> query) =>
        query
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.WaterbusService)
            .Include(x => x.AssignedVessel)
            .ThenInclude(x => x!.RentalPrices)
            .Include(x => x.AssignedManagerUser)
            .Include(x => x.StaffAssignments)
            .ThenInclude(x => x.StaffUser)
            .Include(x => x.OperationServices)
            .Include(x => x.ItineraryStops)
            .ThenInclude(x => x.Station)
            .Include(x => x.Quote);

    public static async Task<User> EnsureCurrentUserCanManageCustomBookingRequestsAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(context, userContext, cancellationToken);
        if (AuthSupport.IsAdmin(actor))
        {
            return actor;
        }

        throw new ForbiddenAccessException();
    }

    public static IQueryable<CustomBookingRequest> ApplyVisibility(
        IQueryable<CustomBookingRequest> query,
        User actor)
    {
        if (AuthSupport.IsAdmin(actor))
        {
            return query;
        }

        if (AuthSupport.IsManager(actor))
        {
            return query.Where(x => x.AssignedManagerUserId == actor.Id);
        }

        if (AuthSupport.IsStaff(actor))
        {
            return query.Where(x => x.StaffAssignments.Any(assignment => assignment.StaffUserId == actor.Id));
        }

        if (AuthSupport.IsCustomer(actor))
        {
            return query.Where(x => x.UserId == actor.Id);
        }

        throw new ForbiddenAccessException();
    }

    public static async Task<IReadOnlyList<RouteSegment>> GetMatchingRouteSegmentsAsync(
        IApplicationDbContext context,
        CustomBookingRequest request,
        CancellationToken cancellationToken)
    {
        var stationIds = GetRouteStationIds(request).Distinct().ToArray();
        if (stationIds.Length < 2)
        {
            return Array.Empty<RouteSegment>();
        }

        return await context.Set<RouteSegment>()
            .Where(x => stationIds.Contains(x.FromStationId) && stationIds.Contains(x.ToStationId))
            .OrderBy(x => x.SegmentOrder)
            .ToListAsync(cancellationToken);
    }

    public static void ApplyRouteEstimate(
        CustomBookingRequest request,
        IReadOnlyCollection<RouteSegment>? routeSegments = null)
    {
        var routeEstimate = CustomBookingRouteEstimator.Estimate(request, routeSegments);
        request.PreferredEndTime = routeEstimate.EstimatedEndTime;
        request.EstimatedEndDate = routeEstimate.EstimatedEndDate;
        request.EstimatedTravelMinutes = routeEstimate.EstimatedTravelMinutes;
        request.EstimatedStayMinutes = routeEstimate.EstimatedStayMinutes;
        request.BufferMinutes = routeEstimate.BufferMinutes;
        request.EstimatedDurationMinutes = routeEstimate.EstimatedDurationMinutes;
    }

    public static async Task<WaterbusService> ResolveVesselRentalServiceAsync(
        IApplicationDbContext context,
        Guid? serviceId,
        CancellationToken cancellationToken)
    {
        WaterbusService? service;
        if (serviceId.HasValue)
        {
            service = await context.WaterbusServices
                .SingleOrDefaultAsync(x => x.Id == serviceId.Value, cancellationToken);
            if (service is null)
            {
                throw AuthSupport.CreateValidationException(
                    nameof(CreateCustomBookingRequestCommand.ServiceId),
                    "Không tìm thấy dịch vụ thuê tàu.");
            }
        }
        else
        {
            service = await context.WaterbusServices
                .Where(x => x.Code == "WT" && x.IsActive && x.BookingMode == BookingMode.VesselRental)
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.Code)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await context.WaterbusServices
                    .Where(x => x.IsActive && x.BookingMode == BookingMode.VesselRental)
                    .OrderBy(x => x.DisplayOrder)
                    .ThenBy(x => x.Code)
                    .FirstOrDefaultAsync(cancellationToken);

            if (service is null)
            {
                throw AuthSupport.CreateValidationException(
                    nameof(CreateCustomBookingRequestCommand.ServiceId),
                    "Chưa có dịch vụ thuê tàu đang hoạt động. Vui lòng cấu hình service có BookingMode = VesselRental.");
            }
        }

        if (!service.IsActive || service.BookingMode != BookingMode.VesselRental)
        {
            throw AuthSupport.CreateValidationException(
                nameof(CreateCustomBookingRequestCommand.ServiceId),
                "Custom booking chỉ được dùng dịch vụ có BookingMode = VesselRental và đang hoạt động.");
        }

        return service;
    }

    private static IEnumerable<Guid> GetRouteStationIds(CustomBookingRequest request)
    {
        if (request.FromStationId.HasValue)
        {
            yield return request.FromStationId.Value;
        }

        foreach (var stop in request.ItineraryStops)
        {
            yield return stop.StationId;
        }

        if (request.ToStationId.HasValue)
        {
            yield return request.ToStationId.Value;
        }
    }

    public static string NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim().ToUpperInvariant();

    public static bool IsValidCurrencyCode(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return true;
        }

        var normalizedCurrency = NormalizeCurrency(currency);
        return normalizedCurrency.Length == 3 && normalizedCurrency.All(char.IsAsciiLetterUpper);
    }

    public static void EnsureValidEmailIfProvided(string? email, string propertyName)
    {
        if (!string.IsNullOrWhiteSpace(email) && !EmailRules.HasAllowedRegistrationDomain(email))
        {
            throw AuthSupport.CreateValidationException(propertyName, EmailRules.AllowedEmailDomainMessage);
        }
    }

    public static string NormalizePhoneOrThrow(string phoneNumber, string propertyName)
    {
        if (!PhoneRules.TryNormalize(phoneNumber, out var normalizedPhone))
        {
            throw AuthSupport.CreateValidationException(propertyName, PhoneRules.InvalidInternationalPhoneMessage);
        }

        return normalizedPhone;
    }

    public static void EnsureDepartureIsInFuture(
        DateOnly departureDate,
        TimeOnly? startTime,
        DateTimeOffset utcNow)
    {
        if (!startTime.HasValue)
        {
            return;
        }

        var vietnamNow = utcNow.ToOffset(VietnamUtcOffset);
        var departure = new DateTimeOffset(
            departureDate.ToDateTime(startTime.Value),
            VietnamUtcOffset);

        if (departure <= vietnamNow)
        {
            throw AuthSupport.CreateValidationException(
                nameof(CreateCustomBookingRequestCommand.DepartureDate),
                "Ngày và giờ khởi hành phải lớn hơn thời điểm hiện tại.");
        }
    }

    public static void EnsureCanQuote(CustomBookingRequest request)
    {
        if (request.Status is not (CustomBookingRequestStatus.PendingReview or CustomBookingRequestStatus.Quoted))
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.Status),
                "Chỉ yêu cầu đang chờ xử lý hoặc đã báo giá mới được cập nhật báo giá.");
        }

        if (request.AssignedVesselId is null || request.AssignedVessel is null)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.AssignedVesselId),
                "Phải gán tàu phù hợp trước khi báo giá.");
        }
    }

    public static DateTimeOffset CalculateQuoteValidUntil(
        CustomBookingRequest request,
        DateTimeOffset utcNow)
    {
        var normalizedNow = utcNow.ToUniversalTime();
        var defaultExpiry = normalizedNow.AddHours(QuoteValidityHours);

        if (!request.PreferredStartTime.HasValue)
        {
            return defaultExpiry;
        }

        var departureAt = new DateTimeOffset(
                request.DepartureDate.ToDateTime(request.PreferredStartTime.Value),
                VietnamUtcOffset)
            .ToUniversalTime();

        if (departureAt <= normalizedNow)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.DepartureDate),
                "Không thể báo giá vì thời gian khởi hành đã qua.");
        }

        return departureAt < defaultExpiry ? departureAt : defaultExpiry;
    }

    public static void EnsureCanAcceptQuote(CustomBookingRequest request, DateTimeOffset now)
    {
        if (request.Status != CustomBookingRequestStatus.Quoted || request.Quote is null)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Status), "Yêu cầu chưa có báo giá để chốt.");
        }

        if (request.Quote.ValidUntil.HasValue && request.Quote.ValidUntil.Value <= now)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Quote.ValidUntil), "Báo giá đã hết hạn.");
        }

        if (request.AssignedVesselId is null)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.AssignedVesselId),
                "Yêu cầu chưa được gán tàu nên chưa thể chấp nhận báo giá.");
        }
    }

    public static void EnsureCanEdit(CustomBookingRequest request)
    {
        if (request.Status != CustomBookingRequestStatus.PendingReview)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.Status),
                "Chỉ yêu cầu đang chờ Admin xem xét mới được chỉnh sửa.");
        }

        if (request.AssignedVesselId.HasValue)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.AssignedVesselId),
                "Admin đã gán tàu. Khách cần liên hệ Admin nếu muốn thay đổi yêu cầu.");
        }
    }

    public static void EnsureCanAssignVessel(CustomBookingRequest request)
    {
        if (request.Status != CustomBookingRequestStatus.PendingReview)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.Status),
                "Chỉ yêu cầu đang chờ xử lý mới được gán hoặc đổi tàu.");
        }
    }

    public static void EnsureCanAssignManager(CustomBookingRequest request)
    {
        if (request.Status != CustomBookingRequestStatus.Confirmed)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.Status),
                "Chỉ được giao Manager sau khi khách đã xác nhận báo giá.");
        }

        if (!request.FromStationId.HasValue)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.FromStationId),
                "Yêu cầu chưa có bến khởi hành để xác định Manager phụ trách.");
        }
    }

    public static void EnsureAssignedManagerCanPlanOperations(CustomBookingRequest request, User actor)
    {
        if (request.Status != CustomBookingRequestStatus.Confirmed)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.Status),
                "Chỉ được phân Staff và dịch vụ vận hành cho yêu cầu đã được khách xác nhận.");
        }

        if (!AuthSupport.IsManager(actor) || request.AssignedManagerUserId != actor.Id)
        {
            throw new ForbiddenAccessException();
        }
    }

    public static void EnsureCanCancel(CustomBookingRequest request)
    {
        if (request.Status is not (CustomBookingRequestStatus.PendingReview or CustomBookingRequestStatus.Quoted))
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.Status),
                "Chỉ yêu cầu đang chờ xử lý hoặc đã báo giá mới được hủy.");
        }
    }

    public static void EnsureVesselMatchesRequest(CustomBookingRequest request, Vessel vessel)
    {
        if (vessel.Status != VesselStatus.Active)
        {
            throw AuthSupport.CreateValidationException(nameof(request.AssignedVesselId), "Tàu được chọn chưa ở trạng thái Active.");
        }

        if (!vessel.SeatsConfigured)
        {
            throw AuthSupport.CreateValidationException(nameof(request.AssignedVesselId), "Tàu được chọn chưa setup xong sơ đồ ghế.");
        }

        if (vessel.NumberOfDecks != request.RequestedNumberOfDecks)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.AssignedVesselId),
                $"Khách yêu cầu tàu {request.RequestedNumberOfDecks} tầng nhưng tàu được chọn có {vessel.NumberOfDecks} tầng.");
        }

        if (vessel.SeatSetupType != request.RequestedSeatSetupType)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.AssignedVesselId),
                $"Khách yêu cầu kiểu ghế {request.RequestedSeatSetupType} nhưng tàu được chọn là {vessel.SeatSetupType}.");
        }

        if (vessel.SeatCount < request.PassengerCount)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.AssignedVesselId),
                $"Tàu chỉ có {vessel.SeatCount} ghế, thấp hơn yêu cầu {request.PassengerCount} khách.");
        }

        if (!vessel.RentalPrices.Any())
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.AssignedVesselId),
                "Tàu được chọn chưa cấu hình giá thuê.");
        }
    }
}
