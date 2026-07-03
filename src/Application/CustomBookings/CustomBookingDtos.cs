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

public sealed record CreateCustomBookingRequest(
    DateOnly DepartureDate,
    BoatRentalUnit RentalUnit,
    int DurationValue,
    int AdultCount,
    int ChildCount,
    TimeOnly? StartTime = null,
    Guid? FromStationId = null,
    Guid? ToStationId = null,
    IReadOnlyList<CreateCustomBookingItineraryStopRequest>? ItineraryStops = null,
    int? PreferredNumberOfDecks = null,
    SeatSetupType? PreferredSeatSetupType = null,
    string? BoatRequirements = null,
    string? PromotionCode = null,
    string? SpecialRequests = null);

public sealed record CreateCustomBookingItineraryStopRequest(
    Guid StationId,
    int StopOrder,
    int StayDurationMinutes,
    string? Note = null);

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CustomBookingQrToken,
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
    int TicketCount,
    IReadOnlyList<CustomBookingTicketDto> Tickets);

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
    string TicketStatus,
    DateTimeOffset IssuedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? CheckedInAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? PassengerId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PassengerName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? PassengerDateOfBirth,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PassengerType);

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CustomBookingQrToken,
    int PassengerCount,
    int RegisteredPassengerCount,
    int AdultCount,
    int ChildCount,
    IReadOnlyList<CustomBookingPassengerDto> Passengers,
    int TicketCount,
    IReadOnlyList<CustomBookingTicketDto> Tickets);

public sealed record ImportCustomBookingPassengersResult(
    Guid BookingId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CustomBookingQrToken,
    int PassengerCount,
    int RegisteredPassengerCount,
    int AdultCount,
    int ChildCount,
    IReadOnlyList<CustomBookingPassengerDto> Passengers,
    int TicketCount,
    IReadOnlyList<CustomBookingTicketDto> Tickets);

public sealed record CustomBookingTicketExportDto(
    Guid BookingId,
    string BookingCode,
    DateOnly? DepartureDate,
    TimeOnly? StartTime,
    string? BoatName,
    string? FromStationName,
    string? ToStationName,
    IReadOnlyList<CustomBookingItineraryStopDto> ItineraryStops,
    IReadOnlyList<CustomBookingTicketExportItemDto> Tickets);

public sealed record CustomBookingTicketExportItemDto(
    Guid TicketId,
    Guid? PassengerId,
    string? PassengerName,
    DateOnly? PassengerDateOfBirth,
    string? PassengerType,
    string TicketCode,
    string QrToken,
    string TicketStatus);

public sealed record CustomBookingTicketSelectionRequest(
    IReadOnlyList<Guid>? TicketIds = null);

public sealed record CustomBookingManifestDto(
    Guid BookingId,
    string BookingCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CustomBookingQrToken,
    string BookingStatus,
    string PaymentStatus,
    string ContactName,
    string ContactPhone,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ContactEmail,
    DateOnly? DepartureDate,
    TimeOnly? StartTime,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BoatName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FromStationName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ToStationName,
    IReadOnlyList<CustomBookingItineraryStopDto> ItineraryStops,
    int PassengerCount,
    int RegisteredPassengerCount,
    int AdultCount,
    int ChildCount,
    CustomBookingTicketSummaryDto TicketSummary,
    IReadOnlyList<CustomBookingManifestPassengerDto> Passengers);

public sealed record CustomBookingTicketSummaryDto(
    int TotalTickets,
    int ActiveTickets,
    int CheckedInTickets,
    int CheckedOutTickets);

public sealed record CustomBookingManifestPassengerDto(
    Guid PassengerId,
    string FullName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? DateOfBirth,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PassengerType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? TicketId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TicketCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TicketStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? CheckedInAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? CheckedInByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CheckedInByName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? CheckedOutAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? CheckedOutByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CheckedOutByName,
    bool CanCheckIn,
    bool CanCheckOut);

public enum CustomBookingAttendanceAction
{
    CheckIn = 0,
    CheckOut = 1
}

public enum CustomBookingAttendanceMode
{
    All = 0,
    Selected = 1
}

public sealed record CustomBookingAttendanceRequest(
    CustomBookingAttendanceAction Action,
    CustomBookingAttendanceMode Mode = CustomBookingAttendanceMode.All,
    IReadOnlyList<Guid>? TicketIds = null);

public sealed record CustomBookingAttendanceResult(
    CustomBookingAttendanceAction Action,
    CustomBookingAttendanceMode Mode,
    int RequestedCount,
    int UpdatedCount,
    int SkippedCount,
    IReadOnlyList<CustomBookingAttendanceSkippedTicketDto> SkippedTickets,
    CustomBookingManifestDto Manifest);

public sealed record CustomBookingAttendanceSkippedTicketDto(
    Guid TicketId,
    string TicketCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? PassengerId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PassengerName,
    string TicketStatus,
    string Reason);

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
