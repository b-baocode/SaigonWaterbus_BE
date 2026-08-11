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
    BoatRentalUnit? RentalUnit,
    int? DurationValue,
    int AdultCount,
    int ChildCount,
    TimeOnly? StartTime = null,
    Guid? FromStationId = null,
    Guid? ToStationId = null,
    IReadOnlyList<CreateCharterBookingItineraryStopRequest>? ItineraryStops = null,
    IReadOnlyList<CreateCharterBookingBoatRequest>? RequestedBoats = null,
    string? SpecialRequests = null,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null,
    bool? InsuranceSelected = null,
    Guid? InsurancePackageId = null);

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
    string? SpecialRequests = null,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null,
    bool? InsuranceSelected = null,
    Guid? InsurancePackageId = null);

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
    Guid FromStationId,
    string FromStationName,
    Guid ToStationId,
    string ToStationName,
    decimal? DistanceKm,
    decimal? TravelMinutes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? MatchedRouteId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? MatchedRouteCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? MatchedRouteName = null);

public sealed record CharterBookingRouteEstimateDto(
    IReadOnlyList<CharterBookingRouteLegEstimateDto> Legs,
    decimal? TotalDistanceKm,
    decimal EstimatedTravelMinutes,
    int EstimatedStayMinutes,
    int FreeStayMinutes,
    int ChargeableStayMinutes,
    decimal EstimatedBufferMinutes,
    decimal EstimatedDurationMinutes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? ChargeableDurationMinutes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? ChargeableDurationValue,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RentalUnit,
    bool HasCompleteDistanceEstimate,
    bool HasCompleteTravelTimeEstimate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? MatchedRouteId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? MatchedRouteCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? MatchedRouteName = null);

public sealed record CharterBookingSelectedRouteDto(
    Guid RouteId,
    string RouteCode,
    string RouteName,
    string RouteType,
    string RouteLabel = "",
    bool IsGeneratedForBooking = false);

public sealed record CharterBookingRoutePlanLegRequest(
    Guid FromStationId,
    Guid ToStationId,
    Guid RouteId);

public sealed record CharterBookingRouteCandidateResult(
    Guid BookingId,
    string BookingCode,
    IReadOnlyList<CharterBookingRouteCandidateLegDto> Legs);

public sealed record CharterBookingRouteCandidateLegDto(
    int LegOrder,
    Guid FromStationId,
    string FromStationName,
    Guid ToStationId,
    string ToStationName,
    IReadOnlyList<CharterBookingRouteCandidateDto> Candidates);

public sealed record CharterBookingRouteCandidateDto(
    Guid RouteId,
    string RouteCode,
    string RouteName,
    string RouteType,
    decimal? DistanceKm,
    decimal? EstimatedDurationMin,
    int FromStopOrder,
    int ToStopOrder,
    string RouteLabel = "",
    bool IsSelectableForCharterQuote = true);

public sealed record CharterBookingInsuranceDto(
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
    DateTimeOffset? HoldExpiresAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharterBookingUserAssignmentDto? AssignedManager = null,
    Guid? BoatId = null);

public sealed record CharterBookingListItemDto(
    Guid Id,
    string BookingCode,
    string BookingStatus,
    string PaymentStatus,
    string DepartureDate,
    string? StartTime,
    string? RentalUnit,
    int? DurationValue,
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
    DateTimeOffset? HoldExpiresAt,
    Guid? FromStationId,
    Guid? ToStationId,
    Guid? BoatId,
    /// <summary>Số tiền cọc gợi ý = 50% TotalAmount khi chưa cọc, = 0 khi đã cọc xong.</summary>
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    decimal SuggestedDepositAmount = 0,
    /// <summary>true nếu booking đã có phần cọc thanh toán thành công.</summary>
    bool HasDepositPaid = false);

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
    string? RentalUnit,
    int? DurationValue,
    CharterBookingRouteEstimateDto RouteEstimate,
    Guid? FromStationId,
    Guid? ToStationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FromStationName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ToStationName,
    IReadOnlyList<CharterBookingItineraryStopDto> ItineraryStops,
    string? SpecialRequests,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PromotionCode,
    decimal TotalAmount,
    decimal DepositAmount,
    decimal RemainingAmount,
    /// <summary>true khi RemainingAmount &gt; 0 — cần thanh toán thêm.</summary>
    bool RequiresAdditionalPayment,
    string ContactName,
    string ContactPhone,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ContactEmail,
    IReadOnlyList<CharterBookingPassengerDto> Passengers,
    IReadOnlyList<CharterBookingPaymentDto> Payments,
    int TicketCount,
    IReadOnlyList<CharterBookingTicketDto> Tickets,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharterBookingUserAssignmentDto? AssignedManager = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharterBookingInsuranceDto? Insurance = null,
    bool InsuranceSelected = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? InsurancePackageId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharterBookingSelectedRouteDto? SelectedRoute = null,
    Guid? BoatId = null,
    /// <summary>Số tiền cọc gợi ý = 50% TotalAmount khi chưa cọc, = 0 khi đã cọc xong.</summary>
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    decimal SuggestedDepositAmount = 0,
    /// <summary>true nếu booking đã có phần cọc thanh toán thành công (DepositAmount &gt; 0).</summary>
    bool HasDepositPaid = false);

public sealed record CharterBookingUserAssignmentDto(
    Guid UserId,
    string FullName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? UserCode);

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
    int? PassengerBirthYear,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PassengerType);

public enum CharterBookingPaymentOption
{
    /// <summary>Đặt cọc 50% (mặc định) hoặc theo <c>depositPercent</c>.</summary>
    Deposit = 0,
    /// <summary>Thanh toán toàn bộ 100% ngay.</summary>
    Full = 1,
    /// <summary>Thanh toán phần còn lại sau khi đã đặt cọc.</summary>
    Remaining = 2
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
    string? DateOfBirth,
    int? BirthYear = null);

public sealed record CharterBookingPassengerDto(
    Guid PassengerId,
    string FullName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? DateOfBirth,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? BirthYear,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PassengerType,
    string ApprovalStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? RequestBatchId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? RequestedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? ReviewedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ReviewNote = null);

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
    IReadOnlyList<CharterBookingTicketDto> Tickets,
    string PaymentStatus,
    decimal TotalAmount,
    decimal DepositAmount,
    decimal RemainingAmount,
    bool RequiresAdditionalPayment,
    decimal AdditionalInsuranceAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharterBookingInsuranceDto? Insurance = null);

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
    IReadOnlyList<CharterBookingTicketDto> Tickets,
    string PaymentStatus,
    decimal TotalAmount,
    decimal DepositAmount,
    decimal RemainingAmount,
    bool RequiresAdditionalPayment,
    decimal AdditionalInsuranceAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharterBookingInsuranceDto? Insurance = null);

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
    int? PassengerBirthYear,
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
    IReadOnlyList<CharterBookingManifestPassengerDto> Passengers,
    Guid? BoatId = null);

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
    int? BirthYear,
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

public sealed record AssignCharterBookingManagerRequest(
    Guid? ManagerUserId);

public sealed record QuoteCharterBookingBoatRequest(
    int BoatOrder,
    Guid BoatId);

public sealed record QuoteCharterBookingRequest(
    Guid? BoatId = null,
    IReadOnlyList<QuoteCharterBookingBoatRequest>? Boats = null,
    IReadOnlyList<CharterBookingRoutePlanLegRequest>? RoutePlan = null);

public enum CharterBookingQuoteResponseAction
{
    Accept = 0,
    RequestChanges = 1,
    Reject = 2
}

public sealed record RespondCharterBookingQuoteRequest(
    CharterBookingQuoteResponseAction Action,
    string? Note = null);

public sealed record UpdateCharterBookingStatusRequest(
    BookingStatus BookingStatus,
    string? Note = null);

public sealed record ReviewCharterBookingPassengerAddRequest(
    string? Note = null);

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
    string? PromotionCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharterBookingInsuranceDto? Insurance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharterBookingSelectedRouteDto? SelectedRoute = null);

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
    DateTimeOffset? HoldExpiresAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharterBookingInsuranceDto? Insurance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharterBookingSelectedRouteDto? SelectedRoute = null);

public sealed record CharterBookingTripDto(
    Guid TripId,
    string TripCode,
    Guid BoatId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BoatName,
    int BoatOrder,
    DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime,
    int CapacitySnapshot,
    string TripStatus);

public sealed record CharterBookingTripStopDto(
    Guid StationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? StationName,
    int StopOrder,
    int StayDurationMinutes,
    DateTimeOffset? PlannedArrivalTime,
    DateTimeOffset? PlannedDepartureTime,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Note);

public sealed record CreateCharterBookingTripResult(
    Guid BookingId,
    string BookingCode,
    Guid RouteId,
    string RouteCode,
    string RouteName,
    IReadOnlyList<CharterBookingTripDto> Trips,
    IReadOnlyList<CharterBookingTripStopDto> Stops);
