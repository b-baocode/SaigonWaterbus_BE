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

    public static IQueryable<CustomBookingRequest> IncludeDetails(IQueryable<CustomBookingRequest> query) =>
        query
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.PreferredVessel)
            .Include(x => x.ItineraryStops)
            .ThenInclude(x => x.Station)
            .Include(x => x.Quote);

    public static async Task<User> EnsureCurrentUserCanManageCustomBookingRequestsAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(context, userContext, cancellationToken);
        if (AuthSupport.IsAdmin(actor) || AuthSupport.IsManager(actor))
        {
            return actor;
        }

        throw new ForbiddenAccessException();
    }

    public static IQueryable<CustomBookingRequest> ApplyVisibility(
        IQueryable<CustomBookingRequest> query,
        User actor)
    {
        if (AuthSupport.IsAdmin(actor) || AuthSupport.IsManager(actor))
        {
            return query;
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

    public static string NormalizeStationCode(string stationCode) => stationCode.Trim().ToUpperInvariant();

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

    public static DateTimeOffset? NormalizeUtc(DateTimeOffset? value) =>
        value?.ToUniversalTime();

    public static DateOnly GetVietnamToday(TimeProvider timeProvider) =>
        DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(VietnamUtcOffset).DateTime);

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

    public static void EnsureDepartureDateIsNotPast(DateOnly departureDate, DateOnly today)
    {
        if (departureDate < today)
        {
            throw AuthSupport.CreateValidationException(
                nameof(CreateCustomBookingRequestCommand.DepartureDate),
                "Ngày đi không được nhỏ hơn ngày hiện tại.");
        }
    }

    public static void EnsurePreferredTimeRangeIsValid(TimeOnly? startTime, TimeOnly? endTime)
    {
        if (startTime.HasValue && endTime.HasValue && startTime.Value >= endTime.Value)
        {
            throw AuthSupport.CreateValidationException(
                "PreferredEndTime",
                "Giờ kết thúc mong muốn phải lớn hơn giờ bắt đầu.");
        }
    }

    public static void EnsureCanQuote(CustomBookingRequest request)
    {
        if (request.Status is CustomBookingRequestStatus.QuoteAccepted
            or CustomBookingRequestStatus.Confirmed
            or CustomBookingRequestStatus.Cancelled)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Status), "Yêu cầu này không thể báo giá lại.");
        }
    }

    public static void EnsureQuoteIsValid(DateTimeOffset? validUntil, DateTimeOffset now)
    {
        if (validUntil.HasValue && validUntil.Value <= now)
        {
            throw AuthSupport.CreateValidationException(
                nameof(QuoteCustomBookingRequestCommand.ValidUntil),
                "Hạn báo giá phải lớn hơn thời điểm hiện tại.");
        }
    }

    public static void EnsureCanAcceptQuote(CustomBookingRequest request, DateTimeOffset now)
    {
        if (request.Status != CustomBookingRequestStatus.Quoted || request.Quote is null)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Status), "Yêu cầu chưa có báo giá để chốt.");
        }

        if (request.Quote.ValidUntil.HasValue && request.Quote.ValidUntil.Value < now)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Quote.ValidUntil), "Báo giá đã hết hạn.");
        }
    }
}
