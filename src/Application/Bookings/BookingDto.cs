using System.Text.Json.Serialization;

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
    string? RouteType);

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
    DateTimeOffset? ReturnDeparture = null);

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
    string? TicketStatus);
