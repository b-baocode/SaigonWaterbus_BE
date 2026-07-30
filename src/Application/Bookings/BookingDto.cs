using System.Text.Json.Serialization;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Bookings;

// ServiceType/RouteType: dịch vụ của đơn (Waterbus | Sightseeing | Charter) suy từ route của
// chuyến — FE dựa vào đây để render đúng màn hình (tour ngắm cảnh đi nguyên chuyến, không có chặng).
public sealed record BookingListItemDto(
    Guid BookingId,
    string BookingCode,
    DateTimeOffset BookedAt,
    string BookingStatus,
    decimal TotalAmount,
    int ItemCount,
    string ServiceType,
    string? RouteType,
    int PointsUsed = 0,
    int PointsEarned = 0,
    BookingInsuranceDto? Insurance = null);

public sealed record BookingDetailDto(
    Guid BookingId,
    string BookingCode,
    DateTimeOffset BookedAt,
    string BookingStatus,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    int PointsUsed,
    int PointsEarned,
    string? PromotionCode,
    IReadOnlyList<BookingItemDto> Items,
    string PaymentStatus,
    string? BookingQrToken,
    DateTimeOffset? HoldExpiresAt,
    IReadOnlyList<BookingPaymentDto> Payments,
    string ServiceType,
    string? RouteType,
    string? ReturnTripCode = null,
    DateTimeOffset? ReturnDeparture = null,
    BookingInsuranceDto? Insurance = null,
    Guid? TripId = null,
    string? BoatCode = null,
    string? BoatName = null,
    Guid? ReturnTripId = null,
    string? ReturnBoatCode = null,
    string? ReturnBoatName = null,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null);

public sealed record BookingInsuranceDto(
    Guid InsurancePackageId,
    string Code,
    string Name,
    string BookingType,
    bool IsRequired,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ProviderName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ProviderLogoUrl,
    decimal UnitPremiumAmount,
    decimal CoverageAmount,
    string Currency,
    int Quantity,
    decimal TotalAmount,
    IReadOnlyList<string> Conditions,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TermsUrl,
    DateTimeOffset QuotedAt,
    bool Selected = true);

internal static class BookingInsuranceDtoMapper
{
    public static BookingInsuranceDto? ToDto(BookingInsuranceSnapshot? snapshot) =>
        snapshot is null
            ? null
            : new BookingInsuranceDto(
                snapshot.InsurancePackageId,
                snapshot.Code,
                snapshot.Name,
                snapshot.BookingType,
                snapshot.IsRequired,
                snapshot.ProviderName,
                snapshot.ProviderLogoUrl,
                snapshot.UnitPremiumAmount,
                snapshot.CoverageAmount,
                snapshot.Currency,
                snapshot.Quantity,
                snapshot.TotalAmount,
                snapshot.Conditions,
                snapshot.TermsUrl,
                snapshot.QuotedAt);
}

public sealed record BookingPaymentDto(
    Guid PaymentId,
    string PaymentCode,
    string? Provider,
    string? ProviderTransactionId,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    string PaymentPurpose,
    string PaymentStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CheckoutUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? QrCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? PaidAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? ExpiresAt,
    decimal RefundAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? RefundRequestedAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundMethod,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundReferenceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundPayoutId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundFailureReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? RefundProcessedByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? RefundedAt);

public sealed record BookingItemDto(
    Guid BookingItemId,
    string TripCode,
    string PassengerName,
    string? PassengerPhone,
    string TicketTypeName,
    string? SeatNumber,
    string FromStationName,
    string ToStationName,
    DateTimeOffset? ScheduledDeparture,
    DateTimeOffset? ScheduledArrival,
    decimal UnitPrice,
    string ItemStatus,
    string? TicketCode,
    string? TicketQrToken,
    string? TicketStatus,
    Guid? FromStationId = null,
    Guid? ToStationId = null,
    bool IsLapInfant = false,
    Guid? CompanionPassengerId = null,
    string? CompanionPassengerName = null,
    bool UsesCompanionTicket = false,
    int? BirthYear = null,
    string? BoatCode = null,
    string? BoatName = null,
    Guid? TripId = null,
    string? PassengerEmail = null);
