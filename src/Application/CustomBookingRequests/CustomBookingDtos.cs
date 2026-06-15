using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record CustomBookingVesselDto(
    Guid Id,
    string Code,
    string Name,
    int SeatCount,
    int PassengerCapacity,
    int NumberOfDecks,
    string ImageUrl);

public sealed record CustomBookingItineraryStopDto(
    Guid Id,
    int StopOrder,
    Guid StationId,
    string StationCode,
    string StationName,
    int StayDurationMinutes,
    string? Note);

public sealed record CustomBookingRouteLegDto(
    int LegOrder,
    string FromStationName,
    string ToStationName,
    decimal? DistanceKm,
    string? TravelDurationText);

public sealed record CustomBookingRouteEstimateDto(
    IReadOnlyCollection<CustomBookingRouteLegDto> Legs,
    decimal? TotalDistanceKm,
    string EstimatedTravelDurationText,
    string EstimatedStayDurationText,
    string EstimatedDurationText,
    DateOnly? EstimatedEndDate,
    TimeOnly? EstimatedEndTime)
{
    public static CustomBookingRouteEstimateDto From(
        CustomBookingRequest request,
        IReadOnlyCollection<RouteSegment>? routeSegments = null)
    {
        var estimate = CustomBookingRouteEstimator.Estimate(request, routeSegments);
        return new CustomBookingRouteEstimateDto(
            estimate.Legs
                .Select(x => new CustomBookingRouteLegDto(
                    x.LegOrder,
                    x.FromStationName,
                    x.ToStationName,
                    x.DistanceKm,
                    x.TravelMinutes.HasValue ? CustomBookingRouteEstimator.FormatDuration(x.TravelMinutes.Value) : null))
                .ToArray(),
            estimate.TotalDistanceKm,
            CustomBookingRouteEstimator.FormatDuration(estimate.EstimatedTravelMinutes),
            CustomBookingRouteEstimator.FormatDuration(estimate.EstimatedStayMinutes),
            CustomBookingRouteEstimator.FormatDuration(estimate.EstimatedDurationMinutes),
            estimate.EstimatedEndDate,
            estimate.EstimatedEndTime);
    }
}

public sealed record CustomBookingRequestDto(
    Guid Id,
    Guid? UserId,
    string ContactName,
    string ContactPhone,
    string? ContactEmail,
    CustomBookingVesselDto? PreferredVessel,
    DateOnly DepartureDate,
    TimeOnly? PreferredStartTime,
    TimeOnly? PreferredEndTime,
    string FromLocation,
    string ToLocation,
    string? FromStationCode,
    string? FromStationName,
    string? ToStationCode,
    string? ToStationName,
    string? ItineraryNote,
    int PassengerCount,
    int AdultCount,
    int ChildCount,
    CustomBookingRouteEstimateDto RouteEstimate,
    IReadOnlyCollection<CustomBookingItineraryStopDto> ItineraryStops,
    string? SpecialRequests,
    CustomBookingRequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? QuotedAt,
    DateTimeOffset? QuoteAcceptedAt,
    CustomBookingQuoteDto? Quote)
{
    public static CustomBookingRequestDto From(
        CustomBookingRequest request,
        IReadOnlyCollection<RouteSegment>? routeSegments = null)
    {
        var routeEstimate = CustomBookingRouteEstimateDto.From(request, routeSegments);

        return new CustomBookingRequestDto(
            request.Id,
            request.UserId,
            request.ContactName,
            request.ContactPhone,
            request.ContactEmail,
            request.PreferredVessel is null ? null : new CustomBookingVesselDto(
                request.PreferredVessel.Id,
                request.PreferredVessel.Code,
                request.PreferredVessel.Name,
                request.PreferredVessel.SeatCount,
                request.PreferredVessel.PassengerCapacity,
                request.PreferredVessel.NumberOfDecks,
                request.PreferredVessel.ImageUrl ?? string.Empty),
            request.DepartureDate,
            request.PreferredStartTime,
            routeEstimate.EstimatedEndTime,
            request.FromLocation,
            request.ToLocation,
            request.FromStationCode,
            request.FromStation?.StationName,
            request.ToStationCode,
            request.ToStation?.StationName,
            request.ItineraryNote,
            request.PassengerCount,
            request.AdultCount,
            request.ChildCount,
            routeEstimate,
            request.ItineraryStops
                .OrderBy(x => x.StopOrder)
                .Select(x => new CustomBookingItineraryStopDto(
                    x.Id,
                    x.StopOrder,
                    x.StationId,
                    x.Station.StationCode,
                    x.Station.StationName,
                    x.StayDurationMinutes,
                    x.Note))
                .ToArray(),
            request.SpecialRequests,
            request.Status,
            request.Created,
            request.QuotedAt,
            request.QuoteAcceptedAt,
            request.Quote is null ? null : CustomBookingQuoteDto.From(request.Quote));
    }
}

public sealed record CustomBookingQuoteDto(
    Guid Id,
    decimal QuotedPrice,
    decimal DepositPercent,
    decimal DepositAmount,
    decimal RemainingAmount,
    string Currency,
    DateTimeOffset? ValidUntil)
{
    public static CustomBookingQuoteDto From(CustomBookingQuote quote) => new(
        quote.Id,
        quote.QuotedPrice,
        quote.DepositPercent,
        quote.DepositAmount,
        quote.RemainingAmount,
        quote.Currency,
        quote.ValidUntil);
}
