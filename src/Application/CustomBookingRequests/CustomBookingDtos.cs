using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record CustomBookingRequestDto(
    Guid Id,
    Guid? UserId,
    Guid? ContactUserId,
    string ContactName,
    string ContactPhone,
    string? ContactEmail,
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
        request.ContactUserId,
        request.ContactName,
        request.ContactPhone,
        request.ContactEmail,
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
    decimal DepositAmount,
    decimal RemainingAmount,
    string Currency,
    string? PriceNote,
    DateTimeOffset? ValidUntil)
{
    public static CustomBookingQuoteDto From(CustomBookingQuote quote) => new(
        quote.Id,
        quote.QuotedPrice,
        quote.DepositAmount,
        quote.RemainingAmount,
        quote.Currency,
        quote.PriceNote,
        quote.ValidUntil);
}
