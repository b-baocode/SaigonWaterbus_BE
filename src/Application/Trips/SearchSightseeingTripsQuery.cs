using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.InsurancePackages;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Trips;

/// <summary>
/// Tìm chuyến ngắm cảnh (route SightseeingLoop) theo ngày khởi hành.
/// Tuyến vòng lặp có bến đầu = bến cuối nên không cần chọn chặng đi;
/// ghế bán nguyên chuyến, giá lấy theo loại ghế hiện hành (không tính theo quãng đường).
/// </summary>
public sealed record SearchSightseeingTripsQuery(
    DateOnly OperatingDate) : IRequest<IReadOnlyList<TripSummaryDto>>;

public sealed class SearchSightseeingTripsQueryHandler : IRequestHandler<SearchSightseeingTripsQuery, IReadOnlyList<TripSummaryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public SearchSightseeingTripsQueryHandler(IApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<TripSummaryDto>> Handle(SearchSightseeingTripsQuery request, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        // Ẩn các chuyến đã qua hạn bán vé (đóng bán trước giờ khởi hành) — khớp với chặn ở tạo booking.
        var bookingCutoff = now + BookingExpirationPolicy.BookingCutoffBeforeDeparture;

        var trips = await _context.Set<Trip>()
            .Include(t => t.Route)
            .Include(t => t.Boat)
            .Where(t => t.Route.RouteType == RouteTypes.SightseeingLoop
                     && t.Route.IsBookable
                     && t.TripType == TripTypes.Regular
                     && t.OperatingDate == request.OperatingDate
                     && TripBookingAvailabilitySupport.CustomerBookableStatuses.Contains(t.TripStatus)
                     && (t.AdjustedDepartureTime ?? t.DepartureTime) > bookingCutoff)
            .ToListAsync(cancellationToken);

        if (trips.Count == 0)
            return [];

        var tripIds = trips.Select(t => t.Id).ToList();

        // Sightseeing bán ghế nguyên chuyến nên mọi vé active đều chiếm ghế —
        // không cần lọc giao chặng như search waterbus thường. Đếm distinct ghế
        // theo TripSeat.TripId (booking khứ hồi có ghế trên 2 trip).
        var bookedCounts = (await _context.Set<BookingPassenger>()
                .Where(p => p.TripSeatId.HasValue && tripIds.Contains(p.TripSeat!.TripId))
                .Where(Bookings.BookingSeatOccupancySupport.PassengerOccupiesSeat(now))
                .Select(p => new { p.TripSeat!.TripId, TripSeatId = p.TripSeatId!.Value })
                .ToListAsync(cancellationToken))
            .GroupBy(p => p.TripId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.TripSeatId).Distinct().Count());

        var boatIds = trips
            .Where(x => x.BoatId.HasValue)
            .Select(x => x.BoatId!.Value)
            .Distinct()
            .ToList();
        var seatRows = await _context.Set<Seat>()
            .Where(x => boatIds.Contains(x.BoatId))
            .Include(x => x.SeatType)
            .Select(x => new { x.BoatId, x.IsActive, x.SeatTypeCode, BasePriceFromDb = (decimal?)x.SeatType!.BasePrice })
            .ToListAsync(cancellationToken);
        var seatStats = seatRows
            .GroupBy(x => x.BoatId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    ActiveSeatCount = g.Count(x => x.IsActive),
                    MinSeatPrice = g
                        .Where(x => x.IsActive)
                        .Select(x => x.BasePriceFromDb ?? (SeatTypePricing.TryGetBasePrice(x.SeatTypeCode, out var p) ? p : (decimal?)null))
                        .Where(p => p.HasValue)
                        .Min()
                });

        // Giá "từ" = giá ADULT (hệ số 1.0). Giá giảm cho CHILD/SENIOR/DISABLED
        // sẽ được tính riêng tại thời điểm booking.
        const decimal adultModifier = 1.0m;
        var fareAdjustments = await FareAdjustmentSupport.GetEffectiveAdjustmentsAsync(
            _context,
            trips.Select(x => x.OperatingDate).ToArray(),
            cancellationToken);
        var defaultInsurancePerSeat = await InsurancePackageSupport.ResolveDefaultWaterbusInsurancePerSeatAsync(
            _context, cancellationToken);

        return trips.OrderBy(t => t.DepartureTime).Select(t =>
        {
            var booked = bookedCounts.GetValueOrDefault(t.Id, 0);
            seatStats.TryGetValue(t.BoatId ?? Guid.Empty, out var stats);
            var capacity = stats?.ActiveSeatCount ?? t.CapacitySnapshot;
            var available = capacity - booked;
            fareAdjustments.TryGetValue(t.OperatingDate, out var fareAdjustment);

            var minBasePrice = stats?.MinSeatPrice is > 0
                ? FareAdjustmentSupport.ApplySurcharge(stats.MinSeatPrice.Value, fareAdjustment)
                : (decimal?)null;
            // minPrice = giá Adult đã bao gồm bảo hiểm (để hiển thị "từ X VND")
            var minPrice = minBasePrice is > 0
                ? (decimal?)PriceRoundingSupport.RoundFare(minBasePrice.Value * adultModifier + defaultInsurancePerSeat)
                : null;

            // Tuyến vòng lặp đi nguyên chuyến: giờ lên = giờ khởi hành, giờ về = giờ kết thúc.
            return new TripSummaryDto(
                t.Id, t.TripCode, t.Route.RouteName, t.Route.RouteType,
                t.DepartureTime, t.ArrivalTime,
                t.AdjustedDepartureTime ?? t.DepartureTime,
                t.AdjustedArrivalTime ?? t.ArrivalTime,
                Math.Max(0, available), capacity,
                minPrice, t.TripStatus.ToString(),
                t.BoatId,
                OperatingStatus: OperatingStatusSupport.ForTrip(t),
                IsBookingClosed: false,
                IsBookable: available > 0,
                FareAdjustment: fareAdjustment,
                DelayInfo: TripDelaySupport.ToDelayInfoDto(t),
                AdjustedDepartureTime: t.AdjustedDepartureTime,
                AdjustedArrivalTime: t.AdjustedArrivalTime);
        }).ToList();
    }
}
