using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CustomBookingRequests;

internal static class CustomBookingAvailability
{
    private static readonly TimeSpan VietnamUtcOffset = TimeSpan.FromHours(7);
    private static readonly CustomBookingRequestStatus[] VesselReservationStatuses =
    [
        CustomBookingRequestStatus.PendingReview,
        CustomBookingRequestStatus.Quoted,
        CustomBookingRequestStatus.Confirmed
    ];

    public static async Task<bool> HasMatchingAvailableVesselAsync(
        IApplicationDbContext context,
        CustomBookingRequest request,
        CancellationToken cancellationToken)
    {
        var unavailableVesselIds = await GetUnavailableVesselIdsAsync(
            context,
            request,
            excludeRequestId: null,
            cancellationToken);
        var unavailableVesselIdArray = unavailableVesselIds.ToArray();

        return await CustomBookingVesselMatcher.Apply(
                context.Set<Vessel>().AsNoTracking(),
                request.RequestedNumberOfDecks,
                request.RequestedSeatSetupType,
                request.RentalUnit,
                request.PassengerCount)
            .Where(x => !unavailableVesselIdArray.Contains(x.Id))
            .AnyAsync(cancellationToken);
    }

    public static async Task<IReadOnlySet<Guid>> GetUnavailableVesselIdsAsync(
        IApplicationDbContext context,
        CustomBookingRequest request,
        Guid? excludeRequestId,
        CancellationToken cancellationToken)
    {
        if (!TryGetTimeWindow(request, out var requestStartAt, out var requestEndAt))
        {
            return new HashSet<Guid>();
        }

        var requestStartDate = ToVietnamDate(requestStartAt);
        var requestEndDate = ToVietnamDate(requestEndAt);
        var reservations = await context.Set<CustomBookingRequest>()
            .AsNoTracking()
            .Where(x => x.AssignedVesselId.HasValue
                        && (!excludeRequestId.HasValue || x.Id != excludeRequestId.Value)
                        && VesselReservationStatuses.Contains(x.Status)
                        && x.PreferredStartTime.HasValue
                        && x.DepartureDate <= requestEndDate
                        && (x.EstimatedEndDate ?? x.DepartureDate) >= requestStartDate)
            .Select(x => new VesselReservation(
                x.Id,
                x.AssignedVesselId!.Value,
                x.DepartureDate,
                x.PreferredStartTime,
                x.EstimatedEndDate,
                x.PreferredEndTime,
                x.EstimatedDurationMinutes))
            .ToArrayAsync(cancellationToken);

        return reservations
            .Where(reservation => TryGetTimeWindow(reservation, out var startAt, out var endAt)
                                  && TimeWindowsOverlap(requestStartAt, requestEndAt, startAt, endAt))
            .Select(x => x.VesselId)
            .ToHashSet();
    }

    public static async Task EnsureVesselAvailableAsync(
        IApplicationDbContext context,
        CustomBookingRequest request,
        Guid vesselId,
        CancellationToken cancellationToken)
    {
        var unavailableVesselIds = await GetUnavailableVesselIdsAsync(
            context,
            request,
            request.Id,
            cancellationToken);

        if (unavailableVesselIds.Contains(vesselId))
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.AssignedVesselId),
                "Tàu được chọn đã có lịch giữ chỗ trong khung giờ yêu cầu.");
        }
    }

    private static bool TryGetTimeWindow(
        CustomBookingRequest request,
        out DateTimeOffset startAt,
        out DateTimeOffset endAt)
    {
        return TryGetTimeWindow(
            request.DepartureDate,
            request.PreferredStartTime,
            request.EstimatedEndDate,
            request.PreferredEndTime,
            request.EstimatedDurationMinutes,
            out startAt,
            out endAt);
    }

    private static bool TryGetTimeWindow(
        VesselReservation reservation,
        out DateTimeOffset startAt,
        out DateTimeOffset endAt)
    {
        return TryGetTimeWindow(
            reservation.DepartureDate,
            reservation.PreferredStartTime,
            reservation.EstimatedEndDate,
            reservation.PreferredEndTime,
            reservation.EstimatedDurationMinutes,
            out startAt,
            out endAt);
    }

    private static bool TryGetTimeWindow(
        DateOnly departureDate,
        TimeOnly? preferredStartTime,
        DateOnly? estimatedEndDate,
        TimeOnly? preferredEndTime,
        int estimatedDurationMinutes,
        out DateTimeOffset startAt,
        out DateTimeOffset endAt)
    {
        startAt = default;
        endAt = default;
        if (!preferredStartTime.HasValue)
        {
            return false;
        }

        startAt = new DateTimeOffset(
            departureDate.ToDateTime(preferredStartTime.Value),
            VietnamUtcOffset).ToUniversalTime();
        endAt = estimatedEndDate.HasValue && preferredEndTime.HasValue
            ? new DateTimeOffset(
                estimatedEndDate.Value.ToDateTime(preferredEndTime.Value),
                VietnamUtcOffset).ToUniversalTime()
            : startAt.AddMinutes(Math.Max(1, estimatedDurationMinutes));

        if (endAt <= startAt)
        {
            endAt = startAt.AddMinutes(Math.Max(1, estimatedDurationMinutes));
        }

        return true;
    }

    private static DateOnly ToVietnamDate(DateTimeOffset value) =>
        DateOnly.FromDateTime(value.ToOffset(VietnamUtcOffset).DateTime);

    private static bool TimeWindowsOverlap(
        DateTimeOffset firstStartAt,
        DateTimeOffset firstEndAt,
        DateTimeOffset secondStartAt,
        DateTimeOffset secondEndAt) =>
        firstStartAt < secondEndAt && secondStartAt < firstEndAt;

    private sealed record VesselReservation(
        Guid RequestId,
        Guid VesselId,
        DateOnly DepartureDate,
        TimeOnly? PreferredStartTime,
        DateOnly? EstimatedEndDate,
        TimeOnly? PreferredEndTime,
        int EstimatedDurationMinutes);
}
