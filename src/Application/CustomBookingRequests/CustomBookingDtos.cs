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
    IReadOnlyCollection<CustomBookingItineraryStopDto> ItineraryStops,
    string? SpecialRequests,
    CustomBookingRequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? QuotedAt,
    DateTimeOffset? QuoteAcceptedAt,
    CustomBookingQuoteDto? Quote)
{
    public static CustomBookingRequestDto From(CustomBookingRequest request) => new(
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
        request.PreferredEndTime,
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

public sealed record CustomBookingQuoteDto(
    Guid Id,
    decimal QuotedPrice,
    decimal DepositPercent,
    decimal DepositAmount,
    decimal RemainingAmount,
    string Currency,
    string? PriceNote,
    DateTimeOffset? ValidUntil)
{
    public static CustomBookingQuoteDto From(CustomBookingQuote quote) => new(
        quote.Id,
        quote.QuotedPrice,
        quote.DepositPercent,
        quote.DepositAmount,
        quote.RemainingAmount,
        quote.Currency,
        quote.PriceNote,
        quote.ValidUntil);
}
