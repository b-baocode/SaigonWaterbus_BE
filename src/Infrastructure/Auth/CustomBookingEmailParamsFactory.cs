using System.Globalization;
using SaigonWaterbus.Application.CustomBookingRequests;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Auth;

internal static class CustomBookingEmailParamsFactory
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    public static IReadOnlyDictionary<string, object?> CreateQuoteParams(
        CustomBookingRequest request,
        IReadOnlyCollection<RouteSegment>? routeSegments = null) =>
        CreateCommonParams(request, statusLabel: "Đã báo giá", routeSegments);

    public static IReadOnlyDictionary<string, object?> CreateConfirmationParams(
        CustomBookingRequest request,
        IReadOnlyCollection<RouteSegment>? routeSegments = null) =>
        CreateCommonParams(request, statusLabel: "Đã chốt thành công", routeSegments);

    private static IReadOnlyDictionary<string, object?> CreateCommonParams(
        CustomBookingRequest request,
        string statusLabel,
        IReadOnlyCollection<RouteSegment>? routeSegments)
    {
        var quote = request.Quote;
        var vessel = request.PreferredVessel;
        var startTimeText = request.PreferredStartTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "Chưa xác định";
        var routeEstimate = CustomBookingRouteEstimator.Estimate(request, routeSegments);
        var routeLegsText = RouteLegsText(request, routeEstimate);
        var routeDetailText = RouteDetailText(request, routeEstimate, routeLegsText);
        var routeTimelineText = RouteTimelineText(request, routeEstimate);
        var endTimeText = routeEstimate.EstimatedEndTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "Chưa xác định";
        var distanceText = FormatDistance(routeEstimate.TotalDistanceKm);
        var travelDurationText = CustomBookingRouteEstimator.FormatDuration(routeEstimate.EstimatedTravelMinutes);
        var stayDurationText = CustomBookingRouteEstimator.FormatDuration(routeEstimate.EstimatedStayMinutes);
        var durationText = CustomBookingRouteEstimator.FormatDuration(routeEstimate.EstimatedDurationMinutes);

        return new Dictionary<string, object?>
        {
            ["requestCode"] = DisplayRequestCode(request),
            ["status"] = request.Status.ToString(),
            ["statusLabel"] = statusLabel,
            ["confirmedAt"] = request.QuoteAcceptedAt?.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            ["contactName"] = request.ContactName,
            ["contactPhone"] = request.ContactPhone,
            ["contactEmail"] = request.ContactEmail,
            ["vesselCode"] = vessel?.Code,
            ["vesselName"] = vessel is null ? "Chưa xác định" : $"{vessel.Code} - {vessel.Name}",
            ["passengerCapacity"] = vessel?.PassengerCapacity.ToString(CultureInfo.InvariantCulture),
            ["departureDate"] = request.DepartureDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["startTime"] = startTimeText,
            ["timelineStartTime"] = startTimeText,
            ["timelineMoveTime"] = startTimeText,
            ["timelineEndTime"] = endTimeText,
            ["fromStationName"] = request.FromStation?.StationName ?? request.FromLocation,
            ["toStationName"] = request.ToStation?.StationName ?? request.ToLocation,
            ["passengerCount"] = request.PassengerCount.ToString(CultureInfo.InvariantCulture),
            ["adultCount"] = request.AdultCount.ToString(CultureInfo.InvariantCulture),
            ["childCount"] = request.ChildCount.ToString(CultureInfo.InvariantCulture),
            ["itineraryText"] = routeDetailText,
            ["routeSummary"] = RouteSummary(request),
            ["routeTimelineText"] = routeTimelineText,
            ["routeDetailText"] = routeDetailText,
            ["routeLegsText"] = routeLegsText,
            ["routeStopCount"] = request.ItineraryStops.Count.ToString(CultureInfo.InvariantCulture),
            ["totalDistanceKm"] = routeEstimate.TotalDistanceKm?.ToString("0.##", CultureInfo.InvariantCulture),
            ["distanceText"] = distanceText,
            ["durationText"] = durationText,
            ["estimatedTravelDurationText"] = travelDurationText,
            ["estimatedStayDurationText"] = stayDurationText,
            ["estimatedDurationText"] = durationText,
            ["estimatedEndDate"] = routeEstimate.EstimatedEndDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["estimatedEndTime"] = endTimeText,
            ["routeEstimateNote"] = routeEstimate.HasCompleteTravelTimeEstimate
                ? "Thời gian là dự kiến và có thể thay đổi theo điều kiện vận hành thực tế."
                : "Chưa đủ tọa độ bến để ước tính đầy đủ thời gian và khoảng cách.",
            ["quotedPrice"] = quote is null ? null : Money(quote.QuotedPrice, quote.Currency),
            ["depositPercent"] = quote?.DepositPercent.ToString("0.##", CultureInfo.InvariantCulture),
            ["depositAmount"] = quote is null ? null : Money(quote.DepositAmount, quote.Currency),
            ["remainingAmount"] = quote is null ? null : Money(quote.RemainingAmount, quote.Currency),
            ["priceNote"] = "Chi phí được ghi nhận theo báo giá đã xác nhận.",
            ["validUntil"] = quote?.ValidUntil?.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
        };
    }

    private static string RouteSummary(CustomBookingRequest request) =>
        string.Join(" -> ", RoutePointNames(request));

    private static string RouteDetailText(
        CustomBookingRequest request,
        CustomBookingRouteEstimate routeEstimate,
        string routeLegsText)
    {
        var endText = routeEstimate.EstimatedEndDate.HasValue && routeEstimate.EstimatedEndTime.HasValue
            ? $"{routeEstimate.EstimatedEndDate.Value:dd/MM/yyyy} {routeEstimate.EstimatedEndTime.Value:HH:mm}"
            : "Chưa xác định";
        var lines = new List<string>
        {
            $"Tổng lộ trình: {RouteSummary(request)}",
            $"Tổng khoảng cách ước tính: {FormatDistance(routeEstimate.TotalDistanceKm)}",
            $"Thời gian di chuyển ước tính: {CustomBookingRouteEstimator.FormatDuration(routeEstimate.EstimatedTravelMinutes)}",
            $"Tổng thời gian dự kiến: {CustomBookingRouteEstimator.FormatDuration(routeEstimate.EstimatedDurationMinutes)}",
            $"Giờ kết thúc dự kiến: {endText}",
            string.Empty,
            "Chi tiết chặng:",
            routeLegsText
        };

        if (routeEstimate.EstimatedStayMinutes > 0)
        {
            lines.Insert(3, $"Thời gian dừng: {CustomBookingRouteEstimator.FormatDuration(routeEstimate.EstimatedStayMinutes)}");
        }

        return string.Join("\n", lines);
    }

    private static string RouteTimelineText(
        CustomBookingRequest request,
        CustomBookingRouteEstimate routeEstimate)
    {
        var endText = routeEstimate.EstimatedEndDate.HasValue && routeEstimate.EstimatedEndTime.HasValue
            ? $"{routeEstimate.EstimatedEndDate.Value:dd/MM/yyyy} {routeEstimate.EstimatedEndTime.Value:HH:mm}"
            : "Chưa xác định";

        return string.Join("\n", new[]
        {
            RouteSummary(request),
            $"Khoảng cách: {FormatDistance(routeEstimate.TotalDistanceKm)}",
            $"Thời gian di chuyển: {CustomBookingRouteEstimator.FormatDuration(routeEstimate.EstimatedTravelMinutes)}",
            $"Dự kiến kết thúc: {endText}"
        });
    }

    private static string RouteLegsText(CustomBookingRequest request, CustomBookingRouteEstimate routeEstimate)
    {
        if (routeEstimate.Legs.Count == 0)
        {
            return "Chưa đủ thông tin chặng.";
        }

        var stops = request.ItineraryStops.OrderBy(x => x.StopOrder).ToArray();
        var lines = new List<string>();

        foreach (var leg in routeEstimate.Legs)
        {
            lines.Add(
                $"Chặng {leg.LegOrder}: {leg.FromStationName} -> {leg.ToStationName}"
                + $" | {FormatDistance(leg.DistanceKm)}"
                + $" | {FormatMinutes(leg.TravelMinutes)}");

            if (leg.LegOrder <= stops.Length)
            {
                var stop = stops[leg.LegOrder - 1];
                var line = $"- Dừng tại {stop.Station.StationName}: {CustomBookingRouteEstimator.FormatDuration(stop.StayDurationMinutes)}";
                lines.Add(string.IsNullOrWhiteSpace(stop.Note) ? line : $"{line} ({stop.Note})");
            }
        }

        return string.Join("\n", lines);
    }

    private static IEnumerable<string> RoutePointNames(CustomBookingRequest request)
    {
        yield return FromStationName(request);

        foreach (var stop in request.ItineraryStops.OrderBy(x => x.StopOrder))
        {
            yield return stop.Station.StationName;
        }

        yield return ToStationName(request);
    }

    private static string FromStationName(CustomBookingRequest request) =>
        request.FromStation?.StationName ?? request.FromLocation;

    private static string ToStationName(CustomBookingRequest request) =>
        request.ToStation?.StationName ?? request.ToLocation;

    private static string FormatDistance(decimal? distanceKm) =>
        distanceKm.HasValue
            ? $"{distanceKm.Value.ToString("0.##", CultureInfo.InvariantCulture)} km"
            : "Chưa xác định";

    private static string FormatMinutes(int? minutes) =>
        minutes.HasValue
            ? CustomBookingRouteEstimator.FormatDuration(minutes.Value)
            : "Chưa xác định";

    private static string Money(decimal amount, string currency) =>
        string.Create(ViCulture, $"{amount:N0} {currency}");

    private static string DisplayRequestCode(CustomBookingRequest request)
    {
        var createdDate = request.Created == default
            ? DateTimeOffset.UtcNow
            : request.Created;
        var suffix = request.Id.ToString("N")[^6..].ToUpperInvariant();
        return $"CB-{createdDate.ToOffset(TimeSpan.FromHours(7)):yyyyMMdd}-{suffix}";
    }
}
