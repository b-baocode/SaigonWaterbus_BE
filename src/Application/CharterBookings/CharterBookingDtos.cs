using System.Text.Json.Serialization;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record CreateCharterBookingResult(
    Guid BookingId,
    string BookingCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BoatName,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    string BookingStatus,
    int RegisteredPassengerCount,
    int RequestedBoatCount,
    IReadOnlyList<CharterBookingRequestedBoatDto> RequestedBoats);

public sealed record CreateCharterBookingRequest(
    DateOnly DepartureDate,
    BoatRentalUnit RentalUnit,
    int DurationValue,
    int AdultCount,
    int ChildCount,
    TimeOnly? StartTime = null,
    Guid? FromStationId = null,
    Guid? ToStationId = null,
    IReadOnlyList<CreateCharterBookingItineraryStopRequest>? ItineraryStops = null,
    IReadOnlyList<CreateCharterBookingBoatRequest>? RequestedBoats = null,
    string? BoatRequirements = null,
    string? SpecialRequests = null,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null);

public sealed record UpdateCharterBookingRequest(
    DateOnly? DepartureDate = null,
    BoatRentalUnit? RentalUnit = null,
    int? DurationValue = null,
    int? AdultCount = null,
    int? ChildCount = null,
    TimeOnly? StartTime = null,
    Guid? FromStationId = null,
    Guid? ToStationId = null,
    IReadOnlyList<CreateCharterBookingItineraryStopRequest>? ItineraryStops = null,
    IReadOnlyList<CreateCharterBookingBoatRequest>? RequestedBoats = null,
    string? BoatRequirements = null,
    string? SpecialRequests = null,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null);

public sealed record CreateCharterBookingItineraryStopRequest(
    Guid StationId,
    int StopOrder,
    int StayDurationMinutes,
    string? Note = null);

public sealed record CreateCharterBookingBoatRequest(
    int NumberOfDecks);

public sealed record CharterBookingRequestedBoatDto(
    int BoatOrder,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? NumberOfDecks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SeatSetupType = null);

public sealed record CharterBookingSelectedBoatDto(
    int BoatOrder,
    Guid BoatId,
    string BoatName,
    string SeatSetupType,
    int NumberOfDecks,
    decimal UnitPrice,
    decimal ChargeableDurationValue,
    decimal SubtotalAmount);

public sealed record CharterBookingRouteLegEstimateDto(
    int LegOrder,
    string FromStationName,
    string ToStationName,
    decimal? DistanceKm,
    int? TravelMinutes);

public sealed record CharterBookingRouteEstimateDto(
    IReadOnlyList<CharterBookingRouteLegEstimateDto> Legs,
    decimal? TotalDistanceKm,
    int EstimatedTravelMinutes,
    int EstimatedStayMinutes,
    int FreeStayMinutes,
    int ChargeableStayMinutes,
    int EstimatedBufferMinutes,
    int EstimatedDurationMinutes,
    int ChargeableDurationMinutes,
    decimal ChargeableDurationValue,
    string RentalUnit,
    bool HasCompleteDistanceEstimate,
    bool HasCompleteTravelTimeEstimate);

public sealed record AdminCharterBookingListItemDto(
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
    string? BoatName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? HoldExpiresAt);

public sealed record CharterBookingListItemDto(
    Guid Id,
    string BookingCode,
    string BookingStatus,
    string PaymentStatus,
    string DepartureDate,
    string? StartTime,
    string RentalUnit,
    int DurationValue,
    int AdultCount,
    int ChildCount,
    int PassengerCount,
    string? FromStationName,
    string? ToStationName,
    string? BoatName,
    decimal? SubtotalAmount,
    [property: JsonPropertyName("finalAmount")]
    decimal? FinalAmount,
    IReadOnlyList<CharterBookingListRequestedBoatDto> RequestedBoats,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? HoldExpiresAt);

public sealed record CharterBookingListRequestedBoatDto(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? NumberOfDecks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SeatSetupType = null);

public sealed record CharterBookingDetailDto(
    Guid Id,
    Guid BookingId,
    string BookingCode,
    [property: JsonPropertyName("charterBookingQrToken")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CharterBookingQrToken,
    DateTimeOffset CreatedAt,
    string BookingStatus,
    string PaymentStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? HoldExpiresAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BoatName,
    int PassengerCount,
    int AdultCount,
    int ChildCount,
    int RequestedBoatCount,
    IReadOnlyList<CharterBookingRequestedBoatDto> RequestedBoats,
    IReadOnlyList<CharterBookingSelectedBoatDto> SelectedBoats,
    string? PreferredSeatSetupType,
    DateOnly DepartureDate,
    TimeOnly? StartTime,
    string RentalUnit,
    int DurationValue,
    CharterBookingRouteEstimateDto RouteEstimate,
    Guid? FromStationId,
    Guid? ToStationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FromStationName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ToStationName,
    IReadOnlyList<CharterBookingItineraryStopDto> ItineraryStops,
    string? BoatRequirements,
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
    IReadOnlyList<CharterBookingPassengerDto> Passengers,
    IReadOnlyList<CharterBookingPaymentDto> Payments,
    int TicketCount,
    IReadOnlyList<CharterBookingTicketDto> Tickets);

public sealed record CharterBookingItineraryStopDto(
    Guid StationId,
    string StationName,
    int StopOrder,
    int StayDurationMinutes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Note);

public sealed record CharterBookingPaymentDto(
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
    string? RefundReferenceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundPayoutId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundFailureReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? RefundedAt);

public sealed record CharterBookingTicketDto(
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

public enum CharterBookingPaymentOption
{
    Deposit = 0,
    Full = 1
}

public sealed record CreateCharterBookingPaymentRequest(
    CharterBookingPaymentOption PaymentOption = CharterBookingPaymentOption.Deposit,
    decimal? DepositPercent = null);

public sealed record CreateCharterBookingPaymentResult(
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
    string? QrCode,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? BookingHoldExpiresAt);

public sealed record SyncCharterBookingPaymentResult(
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
    DateTimeOffset? PaidAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? BookingHoldExpiresAt);

public sealed record CharterBookingPaymentWebhookResult(
    bool Processed,
    long OrderCode,
    string? PaymentStatus,
    string Message);

public sealed record CharterBookingPassengerRequest(
    string FullName,
    string? DateOfBirth);

public sealed record CharterBookingPassengerDto(
    Guid PassengerId,
    string FullName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? DateOfBirth,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PassengerType);

public sealed record UpdateCharterBookingPassengersRequest(
    IReadOnlyList<CharterBookingPassengerRequest> Passengers);

public sealed record UpdateCharterBookingPassengersResult(
    Guid BookingId,
    [property: JsonPropertyName("charterBookingQrToken")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CharterBookingQrToken,
    int PassengerCount,
    int RegisteredPassengerCount,
    int AdultCount,
    int ChildCount,
    IReadOnlyList<CharterBookingPassengerDto> Passengers,
    int TicketCount,
    IReadOnlyList<CharterBookingTicketDto> Tickets);

public sealed record ImportCharterBookingPassengersResult(
    Guid BookingId,
    [property: JsonPropertyName("charterBookingQrToken")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CharterBookingQrToken,
    int PassengerCount,
    int RegisteredPassengerCount,
    int AdultCount,
    int ChildCount,
    IReadOnlyList<CharterBookingPassengerDto> Passengers,
    int TicketCount,
    IReadOnlyList<CharterBookingTicketDto> Tickets);

public sealed record CharterBookingTicketExportDto(
    Guid BookingId,
    string BookingCode,
    DateOnly? DepartureDate,
    TimeOnly? StartTime,
    string? BoatName,
    string? FromStationName,
    string? ToStationName,
    IReadOnlyList<CharterBookingItineraryStopDto> ItineraryStops,
    IReadOnlyList<CharterBookingTicketExportItemDto> Tickets);

public sealed record CharterBookingTicketExportItemDto(
    Guid TicketId,
    Guid? PassengerId,
    string? PassengerName,
    DateOnly? PassengerDateOfBirth,
    string? PassengerType,
    string TicketCode,
    string QrToken,
    string TicketStatus);

public sealed record CharterBookingTicketSelectionRequest(
    IReadOnlyList<Guid>? TicketIds = null);

public sealed record CharterBookingManifestDto(
    Guid BookingId,
    string BookingCode,
    [property: JsonPropertyName("charterBookingQrToken")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CharterBookingQrToken,
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
    IReadOnlyList<CharterBookingItineraryStopDto> ItineraryStops,
    int PassengerCount,
    int RegisteredPassengerCount,
    int AdultCount,
    int ChildCount,
    CharterBookingTicketSummaryDto TicketSummary,
    IReadOnlyList<CharterBookingManifestPassengerDto> Passengers);

public sealed record CharterBookingTicketSummaryDto(
    int TotalTickets,
    int ActiveTickets,
    int CheckedInTickets,
    int CheckedOutTickets);

public sealed record CharterBookingManifestPassengerDto(
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

public enum CharterBookingAttendanceAction
{
    CheckIn = 0,
    CheckOut = 1
}

public enum CharterBookingAttendanceMode
{
    All = 0,
    Selected = 1
}

public sealed record CharterBookingAttendanceRequest(
    CharterBookingAttendanceAction Action,
    CharterBookingAttendanceMode Mode = CharterBookingAttendanceMode.All,
    IReadOnlyList<Guid>? TicketIds = null);

public sealed record CharterBookingAttendanceResult(
    CharterBookingAttendanceAction Action,
    CharterBookingAttendanceMode Mode,
    int RequestedCount,
    int UpdatedCount,
    int SkippedCount,
    IReadOnlyList<CharterBookingAttendanceSkippedTicketDto> SkippedTickets,
    CharterBookingManifestDto Manifest);

public sealed record CharterBookingAttendanceSkippedTicketDto(
    Guid TicketId,
    string TicketCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? PassengerId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PassengerName,
    string TicketStatus,
    string Reason);

public sealed record QuoteCharterBookingBoatRequest(
    int BoatOrder,
    Guid BoatId);

public sealed record QuoteCharterBookingRequest(
    Guid? BoatId = null,
    IReadOnlyList<QuoteCharterBookingBoatRequest>? Boats = null,
    decimal? SubtotalAmount = null,
    BoatRentalUnit? RentalUnit = null,
    int? DurationValue = null,
    string? PromotionCode = null);

public sealed record UpdateCharterBookingStatusRequest(
    BookingStatus BookingStatus);

public sealed record PreviewCharterBookingQuoteResult(
    Guid BookingId,
    string BookingCode,
    IReadOnlyList<CharterBookingSelectedBoatDto> Boats,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    string PricingSource,
    CharterBookingRouteEstimateDto RouteEstimate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PromotionCode);

public sealed record QuoteCharterBookingResult(
    Guid BookingId,
    string BookingCode,
    Guid BoatId,
    string BoatName,
    IReadOnlyList<CharterBookingSelectedBoatDto> Boats,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    decimal UnitPrice,
    decimal ChargeableDurationValue,
    string PricingSource,
    CharterBookingRouteEstimateDto RouteEstimate,
    string BookingStatus,
    string PaymentStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PromotionCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? HoldExpiresAt);
