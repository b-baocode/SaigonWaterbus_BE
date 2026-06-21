using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public static class CustomBookingVesselReservations
{
    private static readonly TimeSpan VietnamUtcOffset = TimeSpan.FromHours(7);
    private static readonly TimeSpan DefaultQuoteHoldDuration = TimeSpan.FromHours(2);

    private static readonly VesselReservationStatus[] ActiveStatuses =
    [
        VesselReservationStatus.Held,
        VesselReservationStatus.PaymentPending,
        VesselReservationStatus.Confirmed
    ];

    public static async Task<IReadOnlySet<Guid>> GetUnavailableVesselIdsAsync(
        IApplicationDbContext context,
        CustomBookingRequest request,
        Guid? excludeRequestId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!TryGetTimeWindow(request, out var requestStartAt, out var requestEndAt))
        {
            return new HashSet<Guid>();
        }

        var reservations = await context.VesselReservations
            .AsNoTracking()
            .Where(x => ActiveStatuses.Contains(x.Status)
                        && (!excludeRequestId.HasValue
                            || x.SourceType != VesselReservationSourceType.CustomBooking
                            || x.SourceId != excludeRequestId.Value)
                        && (x.Status == VesselReservationStatus.Confirmed
                            || !x.ExpiresAt.HasValue
                            || x.ExpiresAt > now)
                        && x.StartAt < requestEndAt
                        && x.EndAt > requestStartAt)
            .Select(x => x.VesselId)
            .ToArrayAsync(cancellationToken);

        return reservations.ToHashSet();
    }

    public static async Task EnsureVesselAvailableAsync(
        IApplicationDbContext context,
        CustomBookingRequest request,
        Guid vesselId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var unavailableVesselIds = await GetUnavailableVesselIdsAsync(
            context,
            request,
            request.Id,
            now,
            cancellationToken);

        if (unavailableVesselIds.Contains(vesselId))
        {
            throw CreateUnavailableException();
        }
    }

    public static async Task<int> ExpireStaleReservationsAsync(
        IApplicationDbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var staleReservations = await context.VesselReservations
            .Where(x => (x.Status == VesselReservationStatus.Held
                         || x.Status == VesselReservationStatus.PaymentPending)
                        && x.ExpiresAt.HasValue
                        && x.ExpiresAt <= now)
            .ToArrayAsync(cancellationToken);

        foreach (var reservation in staleReservations)
        {
            reservation.Status = VesselReservationStatus.Expired;
            reservation.ReleasedAt ??= now;
            reservation.ReleaseReason ??= "Reservation expired.";
        }

        return staleReservations.Length;
    }

    public static async Task HoldForQuoteAsync(
        IApplicationDbContext context,
        CustomBookingRequest request,
        Guid vesselId,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var expiresAt = ResolveHoldExpiry(now, request);
        await UpsertCustomBookingReservationAsync(
            context,
            request,
            vesselId,
            VesselReservationStatus.Held,
            expiresAt,
            actorId,
            now,
            cancellationToken);
    }

    public static async Task HoldUntilQuoteExpiryAsync(
        IApplicationDbContext context,
        CustomBookingRequest request,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!request.AssignedVesselId.HasValue)
        {
            return;
        }

        await UpsertCustomBookingReservationAsync(
            context,
            request,
            request.AssignedVesselId.Value,
            VesselReservationStatus.Held,
            request.Quote?.ValidUntil ?? ResolveHoldExpiry(now, request),
            actorId,
            now,
            cancellationToken);
    }

    public static async Task MarkPaymentPendingAsync(
        IApplicationDbContext context,
        CustomBookingRequest request,
        Guid actorId,
        DateTimeOffset? expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!request.AssignedVesselId.HasValue)
        {
            return;
        }

        await UpsertCustomBookingReservationAsync(
            context,
            request,
            request.AssignedVesselId.Value,
            VesselReservationStatus.PaymentPending,
            expiresAt ?? ResolveHoldExpiry(now, request),
            actorId,
            now,
            cancellationToken);
    }

    public static async Task ConfirmAsync(
        IApplicationDbContext context,
        CustomBookingRequest request,
        Guid? actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!request.AssignedVesselId.HasValue)
        {
            return;
        }

        var reservation = await UpsertCustomBookingReservationAsync(
            context,
            request,
            request.AssignedVesselId.Value,
            VesselReservationStatus.Confirmed,
            null,
            actorId,
            now,
            cancellationToken);
        reservation.ConfirmedAt ??= now;
        reservation.ConfirmedByUserId ??= actorId;
        reservation.ReleasedAt = null;
        reservation.ReleaseReason = null;
    }

    public static async Task ReleaseAsync(
        IApplicationDbContext context,
        Guid customBookingRequestId,
        VesselReservationStatus status,
        DateTimeOffset now,
        string reason,
        CancellationToken cancellationToken)
    {
        var reservation = await context.VesselReservations
            .SingleOrDefaultAsync(x =>
                    x.SourceType == VesselReservationSourceType.CustomBooking
                    && x.SourceId == customBookingRequestId,
                cancellationToken);

        if (reservation is null)
        {
            return;
        }

        reservation.Status = status;
        reservation.ExpiresAt = null;
        reservation.ReleasedAt = now;
        reservation.ReleaseReason = reason;
    }

    public static bool TryGetTimeWindow(
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

    public static SaigonWaterbus.Application.Common.Exceptions.ValidationException CreateUnavailableException() =>
        AuthSupport.CreateValidationException(
            nameof(CustomBookingRequest.AssignedVesselId),
            "Tàu đã được giữ hoặc xác nhận cho booking khác trong khung giờ này.");

    private static async Task<VesselReservation> UpsertCustomBookingReservationAsync(
        IApplicationDbContext context,
        CustomBookingRequest request,
        Guid vesselId,
        VesselReservationStatus status,
        DateTimeOffset? expiresAt,
        Guid? actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!TryGetTimeWindow(request, out var startAt, out var endAt))
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.PreferredStartTime),
                "Yêu cầu chưa có khung giờ để giữ tàu.");
        }

        var reservation = await context.VesselReservations
            .SingleOrDefaultAsync(x =>
                    x.SourceType == VesselReservationSourceType.CustomBooking
                    && x.SourceId == request.Id,
                cancellationToken);

        if (reservation is null)
        {
            reservation = new VesselReservation
            {
                SourceType = VesselReservationSourceType.CustomBooking,
                SourceId = request.Id,
                CreatedByUserId = actorId
            };
            context.VesselReservations.Add(reservation);
        }

        reservation.VesselId = vesselId;
        reservation.StartAt = startAt;
        reservation.EndAt = endAt;
        reservation.Status = status;
        reservation.ExpiresAt = status == VesselReservationStatus.Confirmed ? null : expiresAt;
        reservation.ReleasedAt = null;
        reservation.ReleaseReason = null;

        return reservation;
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

    private static DateTimeOffset ResolveHoldExpiry(
        DateTimeOffset now,
        CustomBookingRequest request)
    {
        var defaultExpiry = now.Add(DefaultQuoteHoldDuration);
        if (!TryGetTimeWindow(request, out var startAt, out _))
        {
            return defaultExpiry;
        }

        return defaultExpiry <= startAt ? defaultExpiry : startAt;
    }
}
