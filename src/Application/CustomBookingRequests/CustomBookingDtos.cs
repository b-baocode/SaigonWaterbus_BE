using System.Text.Json.Serialization;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record CustomBookingVesselDto(
    Guid Id,
    string Code,
    string Name,
    int SeatCount,
    int NumberOfDecks,
    SeatSetupType SeatSetupType,
    string ImageUrl);

public sealed record CustomBookingServiceDto(
    Guid Id,
    string Code,
    string Name,
    BookingMode BookingMode);

public sealed record CustomBookingAssignedUserDto(
    Guid UserId,
    string FullName);

public sealed record CustomBookingStaffAssignmentDto(
    Guid Id,
    CustomBookingAssignedUserDto Staff,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DutyNote,
    DateTimeOffset AssignedAt);

public sealed record CustomBookingOperationServiceDto(
    Guid Id,
    string ServiceName,
    int Quantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Note);

public sealed record CustomBookingItineraryStopDto(
    Guid Id,
    int StopOrder,
    Guid StationId,
    string StationCode,
    string StationName,
    int StayDurationMinutes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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
    string EstimatedBufferDurationText,
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
            CustomBookingRouteEstimator.FormatDuration(estimate.BufferMinutes),
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ContactEmail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CustomBookingServiceDto? Service,
    int RequestedNumberOfDecks,
    SeatSetupType RequestedSeatSetupType,
    VesselRentalUnit RentalUnit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CustomBookingVesselDto? PreferredVessel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CustomBookingVesselDto? AssignedVessel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CustomBookingAssignedUserDto? AssignedManager,
    IReadOnlyCollection<CustomBookingStaffAssignmentDto> StaffAssignments,
    IReadOnlyCollection<CustomBookingOperationServiceDto> OperationServices,
    DateOnly DepartureDate,
    TimeOnly? PreferredStartTime,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FromStationCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FromStationName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ToStationCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ToStationName,
    int PassengerCount,
    int AdultCount,
    int ChildCount,
    PassengerManifestStatus PassengerManifestStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? PassengerManifestCompletedAt,
    CustomBookingRouteEstimateDto RouteEstimate,
    IReadOnlyCollection<CustomBookingItineraryStopDto> ItineraryStops,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SpecialRequests,
    CustomBookingRequestStatus Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? StatusReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CustomBookingQuoteDto? Quote,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CustomBookingTicketQrDto? Ticket = null)
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
            request.WaterbusService is null ? null : new CustomBookingServiceDto(
                request.WaterbusService.Id,
                request.WaterbusService.Code,
                request.WaterbusService.Name,
                request.WaterbusService.BookingMode),
            request.RequestedNumberOfDecks,
            request.RequestedSeatSetupType,
            request.RentalUnit,
            request.PreferredVessel is null ? null : new CustomBookingVesselDto(
                request.PreferredVessel.Id,
                request.PreferredVessel.Code,
                request.PreferredVessel.Name,
                request.PreferredVessel.SeatCount,
                request.PreferredVessel.NumberOfDecks,
                request.PreferredVessel.SeatSetupType,
                request.PreferredVessel.ImageUrl ?? string.Empty),
            request.AssignedVessel is null ? null : new CustomBookingVesselDto(
                request.AssignedVessel.Id,
                request.AssignedVessel.Code,
                request.AssignedVessel.Name,
                request.AssignedVessel.SeatCount,
                request.AssignedVessel.NumberOfDecks,
                request.AssignedVessel.SeatSetupType,
                request.AssignedVessel.ImageUrl ?? string.Empty),
            request.AssignedManagerUser is null
                ? null
                : new CustomBookingAssignedUserDto(
                    request.AssignedManagerUser.Id,
                    request.AssignedManagerUser.FullName),
            request.StaffAssignments
                .OrderBy(x => x.StaffUser.FullName)
                .Select(x => new CustomBookingStaffAssignmentDto(
                    x.Id,
                    new CustomBookingAssignedUserDto(x.StaffUserId, x.StaffUser.FullName),
                    x.DutyNote,
                    x.AssignedAt))
                .ToArray(),
            request.OperationServices
                .OrderBy(x => x.ServiceName)
                .Select(x => new CustomBookingOperationServiceDto(
                    x.Id,
                    x.ServiceName,
                    x.Quantity,
                    x.Note))
                .ToArray(),
            request.DepartureDate,
            request.PreferredStartTime,
            request.FromStationCode,
            request.FromStation?.StationName,
            request.ToStationCode,
            request.ToStation?.StationName,
            request.PassengerCount,
            request.AdultCount,
            request.ChildCount,
            request.PassengerManifestStatus,
            request.PassengerManifestCompletedAt,
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
            request.StatusReason,
            request.Quote is null ? null : CustomBookingQuoteDto.From(request.Quote));
    }
}

public sealed record CustomBookingQuoteDto(
    Guid Id,
    decimal QuotedPrice,
    decimal ServiceFeeAmount,
    decimal DepositPercent,
    decimal DepositAmount,
    decimal RemainingAmount,
    string Currency,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PriceNote,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? ValidUntil)
{
    public static CustomBookingQuoteDto From(CustomBookingQuote quote) => new(
        quote.Id,
        quote.QuotedPrice,
        quote.ServiceFeeAmount,
        quote.DepositPercent,
        quote.DepositAmount,
        quote.RemainingAmount,
        quote.Currency,
        quote.PriceNote,
        quote.ValidUntil);
}
