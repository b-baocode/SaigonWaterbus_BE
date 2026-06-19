using System.Globalization;
using System.Net;
using System.Text;
using SaigonWaterbus.Application.CustomBookingRequests;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Auth;

internal static class CustomBookingEmailParamsFactory
{
    private const string QrPayloadPrefix = "swb:custom-booking:";
    private const string QrImagePath = "/api/custom-booking-requests/tickets/qr-image";
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    public static IReadOnlyDictionary<string, object?> CreateQuoteParams(
        CustomBookingRequest request,
        IReadOnlyCollection<RouteSegment>? routeSegments = null) =>
        CreateCommonParams(request, QuoteStatusLabel(request), routeSegments, isConfirmation: false);

    public static IReadOnlyDictionary<string, object?> CreateConfirmationParams(
        CustomBookingRequest request,
        IReadOnlyCollection<RouteSegment>? routeSegments = null,
        string? publicApiBaseUrl = null) =>
        CreateCommonParams(
            request,
            statusLabel: "Đã chốt thành công",
            routeSegments,
            isConfirmation: true,
            publicApiBaseUrl);

    private static IReadOnlyDictionary<string, object?> CreateCommonParams(
        CustomBookingRequest request,
        string statusLabel,
        IReadOnlyCollection<RouteSegment>? routeSegments,
        bool isConfirmation,
        string? publicApiBaseUrl = null)
    {
        var quote = request.Quote;
        var vessel = request.AssignedVessel;
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
        var activeTicket = isConfirmation
            ? request.Tickets
                .Where(x => x.Status == CustomBookingTicketStatus.Active)
                .OrderByDescending(x => x.QrIssuedAt)
                .FirstOrDefault()
            : null;
        var qrPayload = activeTicket?.QrToken is null
            ? null
            : $"{QrPayloadPrefix}{activeTicket.QrToken}";
        var qrImageUrl = CreateQrImageUrl(publicApiBaseUrl, qrPayload);
        var isPaymentConfirmed = !isConfirmation
            && request.Status == CustomBookingRequestStatus.Confirmed
            && quote?.DepositPaymentStatus == CustomBookingDepositPaymentStatus.Paid;
        var totalPaidAmount = quote is not null && (isConfirmation || isPaymentConfirmed)
            ? Money(CalculatePaidAmount(quote), quote.Currency)
            : null;
        var baseVesselPrice = quote is null
            ? null
            : Money(quote.QuotedPrice + quote.DiscountAmount - quote.ServiceFeeAmount, quote.Currency);
        var serviceFeeAmount = quote is null || quote.ServiceFeeAmount <= 0
            ? null
            : Money(quote.ServiceFeeAmount, quote.Currency);
        var paymentSummaryAmount = quote is null
            ? null
            : isConfirmation || (isPaymentConfirmed && quote.RemainingAmount <= 0)
                ? totalPaidAmount
                : Money(quote.RemainingAmount, quote.Currency);
        var paymentSummaryLabel = isConfirmation || (isPaymentConfirmed && quote?.RemainingAmount <= 0)
            ? "Đã thanh toán"
            : "Còn lại";

        return new Dictionary<string, object?>
        {
            ["requestCode"] = DisplayRequestCode(request),
            ["status"] = request.Status.ToString(),
            ["statusLabel"] = statusLabel,
            ["bookingDate"] = FormatDateTimeOffset(request.Created, "dd/MM/yyyy"),
            ["confirmedAt"] = request.QuoteAcceptedAt?.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            ["contactName"] = request.ContactName,
            ["contactPhone"] = request.ContactPhone,
            ["contactEmail"] = request.ContactEmail,
            ["vesselCode"] = vessel?.Code,
            ["vesselName"] = vessel is null
                ? $"Chưa gán tàu ({request.RequestedNumberOfDecks} tầng, {SeatSetupTypeLabel(request.RequestedSeatSetupType)})"
                : $"{vessel.Code} - {vessel.Name}",
            ["seatCount"] = vessel?.SeatCount.ToString(CultureInfo.InvariantCulture),
            ["passengerCapacity"] = vessel?.SeatCount.ToString(CultureInfo.InvariantCulture),
            ["requestedNumberOfDecks"] = request.RequestedNumberOfDecks.ToString(CultureInfo.InvariantCulture),
            ["requestedSeatSetupType"] = request.RequestedSeatSetupType.ToString(),
            ["requestedSeatSetupTypeLabel"] = SeatSetupTypeLabel(request.RequestedSeatSetupType),
            ["departureDate"] = request.DepartureDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["startTime"] = startTimeText,
            ["timelineStartTime"] = startTimeText,
            ["timelineMoveTime"] = startTimeText,
            ["timelineEndTime"] = endTimeText,
            ["fromStationName"] = request.FromStation?.StationName ?? request.FromLocation,
            ["toStationName"] = request.ToStation?.StationName ?? request.ToLocation,
            ["fromStationAddress"] = FromStationAddress(request),
            ["toStationAddress"] = ToStationAddress(request),
            ["passengerCount"] = request.PassengerCount.ToString(CultureInfo.InvariantCulture),
            ["adultCount"] = request.AdultCount.ToString(CultureInfo.InvariantCulture),
            ["childCount"] = request.ChildCount.ToString(CultureInfo.InvariantCulture),
            ["itineraryText"] = routeDetailText,
            ["routeShortName"] = RouteShortName(request),
            ["routeSummary"] = RouteSummary(request),
            ["routeTimelineText"] = routeTimelineText,
            ["routeDetailText"] = routeDetailText,
            ["routeLegsText"] = routeLegsText,
            ["middleBoardingRows"] = MiddleBoardingRows(request),
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
            ["baseVesselPrice"] = baseVesselPrice,
            ["serviceFeeAmount"] = serviceFeeAmount,
            ["hasServiceFee"] = quote?.ServiceFeeAmount > 0,
            ["discountCode"] = quote?.DiscountCode,
            ["discountAmount"] = quote is null || quote.DiscountAmount <= 0
                ? null
                : Money(quote.DiscountAmount, quote.Currency),
            ["hasDiscount"] = quote?.DiscountAmount > 0,
            ["specialRequests"] = request.SpecialRequests,
            ["depositPercent"] = quote?.DepositPercent.ToString("0.##", CultureInfo.InvariantCulture),
            ["depositAmount"] = quote is null ? null : Money(quote.DepositAmount, quote.Currency),
            ["remainingAmount"] = quote is null ? null : Money(quote.RemainingAmount, quote.Currency),
            ["paidAmount"] = totalPaidAmount,
            ["totalPaidAmount"] = totalPaidAmount,
            ["paymentSummaryLabel"] = paymentSummaryLabel,
            ["paymentSummaryAmount"] = paymentSummaryAmount,
            ["priceNote"] = quote?.PriceNote ?? "Chi phí được ghi nhận theo báo giá đã xác nhận.",
            ["validUntil"] = quote?.ValidUntil?.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            ["ticketCode"] = activeTicket?.TicketCode,
            ["qrPayload"] = qrPayload,
            ["qrImageUrl"] = qrImageUrl,
            ["qrCodeUrl"] = qrImageUrl
        };
    }

    private static string QuoteStatusLabel(CustomBookingRequest request) =>
        request.Status == CustomBookingRequestStatus.Confirmed
        && request.Quote?.DepositPaymentStatus == CustomBookingDepositPaymentStatus.Paid
            ? "Đã xác nhận thanh toán"
            : "Đã báo giá";

    private static string FormatDateTimeOffset(DateTimeOffset value, string format)
    {
        var displayValue = value == default ? DateTimeOffset.UtcNow : value;
        return displayValue.ToOffset(TimeSpan.FromHours(7)).ToString(format, CultureInfo.InvariantCulture);
    }

    private static string RouteSummary(CustomBookingRequest request) =>
        string.Join(" -> ", RoutePointNames(request));

    private static string RouteShortName(CustomBookingRequest request) =>
        string.Join(" - ", RoutePointShortNames(request));

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

    private static IEnumerable<string> RoutePointShortNames(CustomBookingRequest request)
    {
        yield return request.FromStation?.StationCode ?? FromStationName(request);

        foreach (var stop in request.ItineraryStops.OrderBy(x => x.StopOrder))
        {
            yield return stop.Station.StationCode;
        }

        yield return request.ToStation?.StationCode ?? ToStationName(request);
    }

    private static string FromStationName(CustomBookingRequest request) =>
        request.FromStation?.StationName ?? request.FromLocation;

    private static string ToStationName(CustomBookingRequest request) =>
        request.ToStation?.StationName ?? request.ToLocation;

    private static string FromStationAddress(CustomBookingRequest request) =>
        string.IsNullOrWhiteSpace(request.FromStation?.Address)
            ? FromStationName(request)
            : request.FromStation.Address;

    private static string ToStationAddress(CustomBookingRequest request) =>
        string.IsNullOrWhiteSpace(request.ToStation?.Address)
            ? ToStationName(request)
            : request.ToStation.Address;

    private static string MiddleBoardingRows(CustomBookingRequest request)
    {
        if (request.ItineraryStops.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var stop in request.ItineraryStops.OrderBy(x => x.StopOrder))
        {
            var stationName = WebUtility.HtmlEncode(stop.Station.StationName);
            var description = WebUtility.HtmlEncode(MiddleStopDescription(stop));
            builder.Append(
                """
                <tr>
                  <td width="28" style="vertical-align:top;padding:2px 0 0 0;">
                    <div style="width:12px;height:12px;border-radius:50%;background:#f1c85d;margin:4px 0 0 0;"></div>
                    <div style="width:2px;height:48px;background:#d3dde3;margin-left:5px;margin-top:4px;"></div>
                  </td>
                  <td style="vertical-align:top;padding:0 0 18px 14px;">
                    <div style="font-size:13px;color:#7c8b95;font-weight:900;text-transform:uppercase;letter-spacing:.5px;">Stop / Điểm dừng</div>
                    <div style="margin-top:6px;font-size:18px;line-height:1.35;color:#283640;font-weight:900;">
                """);
            builder.Append(stationName);
            builder.Append(
                """
                    </div>
                    <div style="margin-top:4px;font-size:14px;line-height:1.55;color:#6b7780;">
                """);
            builder.Append(description);
            builder.Append(
                """
                    </div>
                  </td>
                </tr>
                """);
        }

        return builder.ToString();
    }

    private static string MiddleStopDescription(CustomBookingItineraryStop stop)
    {
        var stayText = CustomBookingRouteEstimator.FormatDuration(stop.StayDurationMinutes);
        if (string.IsNullOrWhiteSpace(stop.Note))
        {
            return $"Dừng theo lịch trình: {stayText}.";
        }

        return $"{stop.Note.Trim()} - {stayText}.";
    }

    private static string? CreateQrImageUrl(string? publicApiBaseUrl, string? qrPayload)
    {
        if (string.IsNullOrWhiteSpace(publicApiBaseUrl) || string.IsNullOrWhiteSpace(qrPayload))
        {
            return null;
        }

        var baseUrl = publicApiBaseUrl.TrimEnd('/');
        return $"{baseUrl}{QrImagePath}?payload={Uri.EscapeDataString(qrPayload)}";
    }

    private static decimal CalculatePaidAmount(CustomBookingQuote quote)
    {
        var amount = 0m;
        if (quote.DepositPaymentStatus == CustomBookingDepositPaymentStatus.Paid)
        {
            amount += quote.DepositAmount;
        }

        if (quote.RemainingPaymentStatus == CustomBookingDepositPaymentStatus.Paid)
        {
            amount += quote.RemainingAmount;
        }

        return Math.Min(amount, quote.QuotedPrice);
    }

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

    private static string SeatSetupTypeLabel(SeatSetupType seatSetupType) =>
        seatSetupType switch
        {
            SeatSetupType.FullStandard => "Toàn bộ ghế Standard",
            SeatSetupType.StandardAndVip => "Ghế Standard + River/Sky",
            _ => seatSetupType.ToString()
        };

    private static string DisplayRequestCode(CustomBookingRequest request)
    {
        var createdDate = request.Created == default
            ? DateTimeOffset.UtcNow
            : request.Created;
        var suffix = request.Id.ToString("N")[^6..].ToUpperInvariant();
        return $"CB-{createdDate.ToOffset(TimeSpan.FromHours(7)):yyyyMMdd}-{suffix}";
    }
}
