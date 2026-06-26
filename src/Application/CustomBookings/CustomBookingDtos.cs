using System.Text.Json.Serialization;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CustomBookings;

public sealed record CreateCustomBookingResult(
    Guid BookingId,
    string BookingCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BoatName,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    string BookingStatus,
    int RegisteredPassengerCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PromotionCode);

public sealed record RentalBoatEstimateListDto(
    int PassengerCount,
    int AdultCount,
    int ChildCount,
    string RentalUnit,
    int DurationValue,
    int BoatCount,
    decimal? MinEstimatedPrice,
    decimal? MaxEstimatedPrice,
    IReadOnlyList<RentalBoatEstimateDto> Boats);

public sealed record RentalBoatEstimateDto(
    string BoatName,
    int SeatCount,
    int NumberOfDecks,
    string SeatSetupType,
    decimal? HourlyRentalPrice,
    decimal? DailyRentalPrice,
    decimal UnitPrice,
    decimal EstimatedPrice,
    string Currency,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ImageUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Description);

public sealed record CustomBookingRouteLegEstimateDto(
    int LegOrder,
    string FromStationName,
    string ToStationName,
    decimal? DistanceKm,
    int? TravelMinutes);

public sealed record CustomBookingRouteEstimateDto(
    IReadOnlyList<CustomBookingRouteLegEstimateDto> Legs,
    decimal? TotalDistanceKm,
    int EstimatedTravelMinutes,
    int EstimatedStayMinutes,
    int EstimatedBufferMinutes,
    int EstimatedDurationMinutes,
    int ChargeableDurationValue,
    string RentalUnit,
    bool HasCompleteDistanceEstimate,
    bool HasCompleteTravelTimeEstimate);

public sealed record AdminCustomBookingListItemDto(
    Guid BookingId,
    string BookingCode,
    DateTimeOffset CreatedAt,
    DateOnly DepartureDate,
    string BookingStatus,
    string PaymentStatus,
    int PassengerCount,
    int AdultCount,
    int ChildCount,
    decimal TotalAmount,
    string ContactName,
    string ContactPhone,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ContactEmail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BoatName);

public sealed record CustomBookingListItemDto(
    Guid BookingId,
    string BookingCode,
    DateTimeOffset CreatedAt,
    DateOnly DepartureDate,
    string BookingStatus,
    decimal TotalAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BoatName);

public sealed record CustomBookingDetailDto(
    Guid BookingId,
    string BookingCode,
    DateTimeOffset CreatedAt,
    string BookingStatus,
    string PaymentStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BoatName,
    int PassengerCount,
    int AdultCount,
    int ChildCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? PreferredNumberOfDecks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PreferredSeatSetupType,
    DateOnly DepartureDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    TimeOnly? StartTime,
    string RentalUnit,
    int DurationValue,
    CustomBookingRouteEstimateDto RouteEstimate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FromStationName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ToStationName,
    IReadOnlyList<CustomBookingItineraryStopDto> ItineraryStops,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BoatRequirements,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SpecialRequests,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PromotionCode,
    decimal TotalAmount,
    string ContactName,
    string ContactPhone,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ContactEmail,
    IReadOnlyList<CustomBookingPassengerDto> Passengers,
    IReadOnlyList<CustomBookingPaymentDto> Payments,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CustomBookingTicketDto? Ticket);

public sealed record CustomBookingItineraryStopDto(
    Guid StationId,
    string StationName,
    int StopOrder,
    int StayDurationMinutes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Note);

public sealed record CustomBookingPaymentDto(
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
    decimal RefundAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundReferenceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundPayoutId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundFailureReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? RefundedAt);

public sealed record CustomBookingTicketDto(
    Guid TicketId,
    string TicketCode,
    string QrToken,
    string TicketTypeCode,
    string TicketTypeName,
    string TicketStatus,
    DateTimeOffset IssuedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? CheckedInAt);

public enum CustomBookingPaymentOption
{
    Deposit = 0,
    Full = 1
}

public sealed record CreateCustomBookingPaymentRequest(
    CustomBookingPaymentOption PaymentOption = CustomBookingPaymentOption.Deposit,
    decimal? DepositPercent = null);

public sealed record CreateCustomBookingPaymentResult(
    Guid BookingId,
    Guid PaymentId,
    string PaymentCode,
    decimal Amount,
    string Currency,
    string PaymentPurpose,
    string PaymentStatus,
    string BookingStatus,
    string BookingPaymentStatus,
    decimal DepositAmount,
    decimal RemainingAmount,
    string? PaymentLinkId,
    string? CheckoutUrl,
    string? QrCode);

public sealed record SyncCustomBookingPaymentResult(
    Guid BookingId,
    Guid PaymentId,
    string PaymentCode,
    decimal Amount,
    string PaymentPurpose,
    string PaymentStatus,
    string BookingStatus,
    string BookingPaymentStatus,
    decimal DepositAmount,
    decimal RemainingAmount,
    string? CheckoutUrl,
    DateTimeOffset? PaidAt);

public sealed record CustomBookingPaymentWebhookResult(
    bool Processed,
    long OrderCode,
    string? PaymentStatus,
    string Message);

public sealed record CustomBookingPassengerRequest(
    string FullName,
    string DateOfBirth);

public sealed record CustomBookingPassengerDto(
    Guid PassengerId,
    string FullName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? DateOfBirth,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PassengerType);

public sealed record UpdateCustomBookingPassengersRequest(
    IReadOnlyList<CustomBookingPassengerRequest> Passengers);

public sealed record UpdateCustomBookingPassengersResult(
    Guid BookingId,
    int PassengerCount,
    int RegisteredPassengerCount,
    int AdultCount,
    int ChildCount,
    IReadOnlyList<CustomBookingPassengerDto> Passengers,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CustomBookingTicketDto? Ticket);

public sealed record ImportCustomBookingPassengersResult(
    Guid BookingId,
    int PassengerCount,
    int RegisteredPassengerCount,
    int AdultCount,
    int ChildCount,
    IReadOnlyList<CustomBookingPassengerDto> Passengers,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CustomBookingTicketDto? Ticket);

public sealed record QuoteCustomBookingRequest(
    Guid BoatId,
    decimal? SubtotalAmount = null,
    BoatRentalUnit? RentalUnit = null,
    int? DurationValue = null,
    string? PromotionCode = null);

public sealed record UpdateCustomBookingStatusRequest(
    BookingStatus BookingStatus);

public sealed record QuoteCustomBookingResult(
    Guid BookingId,
    string BookingCode,
    Guid BoatId,
    string BoatName,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    decimal UnitPrice,
    int ChargeableDurationValue,
    string PricingSource,
    CustomBookingRouteEstimateDto RouteEstimate,
    string BookingStatus,
    string PaymentStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PromotionCode);
