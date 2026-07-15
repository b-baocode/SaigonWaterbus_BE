namespace SaigonWaterbus.Application.Bookings;

public sealed record BookingListItemDto(
    Guid BookingId,
    string BookingCode,
    DateTimeOffset BookedAt,
    string BookingStatus,
    decimal TotalAmount,
    int ItemCount);

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
    string? ReturnTripCode = null,
    DateTimeOffset? ReturnDeparture = null);

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
