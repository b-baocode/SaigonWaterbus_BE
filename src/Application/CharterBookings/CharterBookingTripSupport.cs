using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingTripSupport
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    public static readonly TimeOnly OperatingDayStartTime = new(7, 0);
    public static readonly TimeOnly OperatingDayEndTime = new(23, 0);

    public static bool IsWithinOperatingStartWindow(TimeOnly startTime) =>
        startTime >= OperatingDayStartTime && startTime < OperatingDayEndTime;

    public static (DateTimeOffset DepartureTime, DateTimeOffset ArrivalTime) ResolveRentalWindowUtc(Booking booking)
    {
        var departureTime = ResolveDepartureTimeUtc(booking);
        return (departureTime, ResolveArrivalTimeUtc(departureTime, booking));
    }

    public static bool HasScheduleOverlap(
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        DateTimeOffset otherStartAt,
        DateTimeOffset otherEndAt) =>
        startAt < otherEndAt && otherStartAt < endAt;

    public static string FormatVietnamWindow((DateTimeOffset DepartureTime, DateTimeOffset ArrivalTime) window) =>
        $"{window.DepartureTime.ToOffset(VietnamOffset):dd/MM/yyyy HH:mm} - "
        + $"{window.ArrivalTime.ToOffset(VietnamOffset):dd/MM/yyyy HH:mm}";

    /// <summary>Chuoi ben cua booking theo thu tu: ben di -> cac diem dung -> ben den.</summary>
    public static IReadOnlyList<Guid> BuildStationSequence(Booking booking)
    {
        var sequence = new List<Guid>();
        if (booking.FromStationId.HasValue)
        {
            sequence.Add(booking.FromStationId.Value);
        }

        sequence.AddRange(booking.ItineraryStops
            .OrderBy(x => x.StopOrder)
            .Select(x => x.StationId));

        if (booking.ToStationId.HasValue)
        {
            sequence.Add(booking.ToStationId.Value);
        }

        return sequence;
    }

    /// <summary>
    /// Route chi duoc coi la khop khi chuoi RouteStops (theo StopOrder) trung het voi chuoi ben
    /// cua booking — dung ben, dung thu tu, dung so luong. Nhieu route cung khop thi uu tien
    /// CharterReference, sau do route tao gan nhat.
    /// </summary>
    public static async Task<Route?> FindMatchingRouteAsync(
        IApplicationDbContext context,
        IReadOnlyList<Guid> stationSequence,
        CancellationToken cancellationToken)
    {
        if (stationSequence.Count < 2)
        {
            return null;
        }

        var firstStationId = stationSequence[0];
        var candidates = await context.Set<Route>()
            .AsNoTracking()
            .Include(x => x.RouteStops)
            .Where(x => x.Status == "Active"
                && (x.RouteType == RouteTypes.CharterReference || x.RouteType == RouteTypes.SightseeingLoop)
                && x.RouteStops.Count == stationSequence.Count
                && x.RouteStops.Any(stop => stop.StationId == firstStationId))
            .ToListAsync(cancellationToken);

        return candidates
            .Where(route => route.RouteStops
                .OrderBy(stop => stop.StopOrder)
                .Select(stop => stop.StationId)
                .SequenceEqual(stationSequence))
            .OrderByDescending(route =>
                string.Equals(route.RouteType, RouteTypes.CharterReference, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(route => route.Created)
            .FirstOrDefault();
    }

    /// <summary>
    /// Lich trinh tung ben cua trip: thu tu ben (ben di -> diem dung -> ben den), thoi gian dung
    /// tai tung diem va gio den/di du kien tinh don tu gio khoi hanh + thoi gian chay tung chang
    /// (tu routeEstimate) + thoi gian dung. Chang nao thieu thoi gian chay thi cac gio sau do = null.
    /// </summary>
    public static IReadOnlyList<TripStopDraft> BuildTripStopSchedule(
        Booking booking,
        DateTimeOffset departureTimeUtc,
        CharterBookingRouteEstimate routeEstimate)
    {
        var points = new List<(Guid StationId, Station? Station, int StayMinutes, string? Note)>();
        if (booking.FromStationId.HasValue)
        {
            points.Add((booking.FromStationId.Value, booking.FromStation, 0, null));
        }

        foreach (var stop in booking.ItineraryStops.OrderBy(x => x.StopOrder))
        {
            points.Add((stop.StationId, stop.Station, stop.StayDurationMinutes, stop.Note));
        }

        if (booking.ToStationId.HasValue)
        {
            points.Add((booking.ToStationId.Value, booking.ToStation, 0, null));
        }

        var legs = routeEstimate.Legs;
        var legsAligned = legs.Count == points.Count - 1;

        var drafts = new List<TripStopDraft>();
        DateTimeOffset? previousDeparture = departureTimeUtc;
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            DateTimeOffset? plannedArrival;
            DateTimeOffset? plannedDeparture;
            if (i == 0)
            {
                plannedArrival = null;
                plannedDeparture = departureTimeUtc;
            }
            else
            {
                var travelMinutes = legsAligned ? legs[i - 1].TravelMinutes : null;
                plannedArrival = previousDeparture.HasValue && travelMinutes.HasValue
                    ? previousDeparture.Value.AddMinutes((double)travelMinutes.Value)
                    : null;
                plannedDeparture = i == points.Count - 1
                    ? null
                    : plannedArrival?.AddMinutes(point.StayMinutes);
                previousDeparture = plannedDeparture;
            }

            drafts.Add(new TripStopDraft(
                point.StationId,
                point.Station,
                i + 1,
                point.StayMinutes,
                point.Note,
                plannedArrival,
                plannedDeparture));
        }

        return drafts;
    }

    public static DateTimeOffset ResolveDepartureTimeUtc(Booking booking)
    {
        var startTime = booking.StartTime ?? OperatingDayStartTime;
        return new DateTimeOffset(booking.DepartureDate!.Value.ToDateTime(startTime), VietnamOffset)
            .ToUniversalTime();
    }

    public static DateTimeOffset ResolveArrivalTimeUtc(DateTimeOffset departureTimeUtc, Booking booking)
    {
        return ResolveArrivalTimeUtc(departureTimeUtc, booking, routeEstimate: null);
    }

    public static DateTimeOffset ResolveArrivalTimeUtc(
        DateTimeOffset departureTimeUtc,
        Booking booking,
        CharterBookingRouteEstimate? routeEstimate)
    {
        var rentalUnit = CharterBookingRoutePricingSupport.ResolveRentalUnit(booking);
        var durationValue = CharterBookingRoutePricingSupport.ResolveRequestedDurationValue(booking);

        if (rentalUnit == BoatRentalUnit.Day)
        {
            var arrivalDate = booking.DepartureDate!.Value.AddDays(durationValue - 1);
            return new DateTimeOffset(arrivalDate.ToDateTime(OperatingDayEndTime), VietnamOffset)
                .ToUniversalTime();
        }

        if (routeEstimate is { HasCompleteTravelTimeEstimate: true, EstimatedDurationMinutes: > 0 })
        {
            return departureTimeUtc.AddMinutes((double)routeEstimate.EstimatedDurationMinutes);
        }

        return departureTimeUtc.AddHours(durationValue);
    }

    public static string BuildTripCode(Booking booking, int boatOrder) =>
        TripCodeSupport.BuildCharterBookingTripCode(booking, boatOrder);

    /// <summary>Huy cac trip da sinh tu charter booking (khi booking bi huy/hoan tien).</summary>
    public static async Task<int> CancelLinkedTripsAsync(
        IApplicationDbContext context,
        Guid bookingId,
        string statusNote,
        DateTimeOffset statusChangedAt,
        CancellationToken cancellationToken)
    {
        var trips = await context.Set<Trip>()
            .Where(x => x.SourceBookingId == bookingId
                && x.TripStatus != TripStatus.Cancelled
                && x.TripStatus != TripStatus.Completed)
            .ToListAsync(cancellationToken);

        foreach (var trip in trips)
        {
            trip.TripStatus = TripStatus.Cancelled;
            trip.StatusNote = statusNote;
            trip.LastStatusChangedAt = statusChangedAt;
        }

        return trips.Count;
    }

    /// <summary>
    /// Tu dong chuyen booking sang <see cref="BookingStatus.Completed"/> khi trip lien ket da hoan tat.
    /// - Sightseeing / Regular: trip Completed → booking Completed luon (diem den da den).
    /// - Charter 1 chieu: trip Completed → booking Completed.
    /// - Charter khu hoi: doi ca trip chinh lan trip ve cung Completed moi chuyen booking.
    /// - Khong co SourceBookingId: bo qua.
    /// - Booking khong o trang thai <see cref="BookingStatus.Confirmed"/>: bo qua
    ///   (giu nguyen trang thai, khong tu y Completed tu PendingPayment/Cancelled/...).
    /// </summary>
    /// <returns>True neu booking duoc cap nhat trong lan goi nay.</returns>
    public static async Task<bool> CompleteLinkedBookingAsync(
        IApplicationDbContext context,
        Trip trip,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!trip.SourceBookingId.HasValue)
        {
            return false;
        }

        var booking = await context.Set<Booking>()
            .Include(x => x.ReturnTrip)
            .SingleOrDefaultAsync(x => x.Id == trip.SourceBookingId.Value, cancellationToken);
        if (booking is null)
        {
            return false;
        }

        // Booking phai da Confirmed (da check-in hoac da approve) moi tu dong Completed.
        if (booking.BookingStatus != BookingStatus.Confirmed)
        {
            return false;
        }

        // Khu hoi: phai doi trip ve cung Completed.
        var hasReturnTrip = booking.ReturnTripId.HasValue && booking.ReturnTrip is not null;
        if (hasReturnTrip && booking.ReturnTrip!.TripStatus != TripStatus.Completed)
        {
            return false;
        }

        booking.BookingStatus = BookingStatus.Completed;
        booking.CompletionSource = $"TripCompleted:{trip.TripCode}";
        booking.CompletedAt = now;
        return true;
    }

    /// <summary>
    /// Khi trip chuyển sang <see cref="TripStatus.Cancelled"/>:
    /// - Charter booking đang <see cref="BookingStatus.Confirmed"/> → auto chuyển sang Cancelled
    ///   (để booking phản ánh đúng trạng thái trip, không kẹt ở Confirmed khi trip đã hủy).
    /// - Booking đã Cancelled/PendingPayment/Refunded/Completed → giữ nguyên (không ghi đè trạng thái đã kết thúc).
    /// - Khứ hồi: cancel 1 chiều là cancel luôn booking vì trip kia cũng vô hiệu theo business rule
    ///   (charter khứ hồi = 1 booking thuê nguyên tàu, trip nào hủy là booking hủy).
    /// - Không có SourceBookingId: bỏ qua (chỉ áp dụng cho charter, không phải ghép vé).
    /// </summary>
    /// <returns>True nếu booking được cập nhật trong lần gọi này.</returns>
    public static async Task<bool> CancelLinkedBookingAsync(
        IApplicationDbContext context,
        Trip trip,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!trip.SourceBookingId.HasValue)
        {
            return false;
        }

        var booking = await context.Set<Booking>()
            .SingleOrDefaultAsync(x => x.Id == trip.SourceBookingId.Value, cancellationToken);
        if (booking is null)
        {
            return false;
        }

        // Chỉ cancel booking đang còn "sống" (Confirmed). Nếu đã kết thúc thì giữ nguyên.
        if (booking.BookingStatus != BookingStatus.Confirmed)
        {
            return false;
        }

        booking.BookingStatus = BookingStatus.Cancelled;
        booking.CompletionSource = $"TripCancelled:{trip.TripCode}";
        booking.CompletedAt = now;
        return true;
    }
}
