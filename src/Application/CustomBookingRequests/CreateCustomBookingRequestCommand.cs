using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record CreateCustomBookingItineraryStopRequest(
    int StopOrder,
    Guid StationId,
    int StayDurationMinutes,
    string? Note = null);

public sealed record CreateCustomBookingRequestCommand(
    bool UseAccountContact = true,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null,
    Guid? ServiceId = null,
    int RequestedNumberOfDecks = 0,
    SeatSetupType RequestedSeatSetupType = default,
    DateOnly DepartureDate = default,
    TimeOnly? PreferredStartTime = null,
    Guid FromStationId = default,
    Guid ToStationId = default,
    int AdultCount = 0,
    int ChildCount = 0,
    string? SpecialRequests = null,
    IReadOnlyCollection<CreateCustomBookingItineraryStopRequest>? ItineraryStops = null)
    : IRequest<CustomBookingRequestDto>, ICustomBookingTripRequest;

public sealed class CreateCustomBookingRequestCommandValidator : AbstractValidator<CreateCustomBookingRequestCommand>
{
    public CreateCustomBookingRequestCommandValidator()
    {
        Include(new CustomBookingTripRequestValidator<CreateCustomBookingRequestCommand>());

        When(x => !x.UseAccountContact, () =>
        {
            RuleFor(x => x.ContactName).NotEmpty().MaximumLength(150);
            RuleFor(x => x.ContactPhone).NotEmpty().MaximumLength(20);
            RuleFor(x => x.ContactEmail)
                .NotEmpty()
                .WithMessage("Email liên hệ là bắt buộc.");
        });

        RuleFor(x => x.ContactName).MaximumLength(150).When(x => x.ContactName is not null);
        RuleFor(x => x.ContactPhone).MaximumLength(20).When(x => x.ContactPhone is not null);
        RuleFor(x => x.ContactEmail)
            .Cascade(CascadeMode.Stop)
            .MaximumLength(255)
            .WithMessage("Email liên hệ không được vượt quá 255 ký tự.")
            .Must(EmailRules.HasAllowedRegistrationDomain)
            .WithMessage(EmailRules.AllowedEmailDomainMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));

        RuleFor(x => x.ServiceId)
            .NotEqual(Guid.Empty)
            .WithMessage("ServiceId không hợp lệ.")
            .When(x => x.ServiceId.HasValue);
    }
}

public sealed class CreateCustomBookingRequestCommandHandler
    : IRequestHandler<CreateCustomBookingRequestCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public CreateCustomBookingRequestCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<CustomBookingRequestDto> Handle(
        CreateCustomBookingRequestCommand request,
        CancellationToken cancellationToken)
    {
        var user = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsCustomer(user))
        {
            throw new ForbiddenAccessException();
        }

        var contactName = ResolveContactName(request, user);
        var contactPhone = ResolveContactPhone(request, user);
        var contactEmail = ResolveContactEmail(request, user);

        var normalizedPhone = CustomBookingRequestSupport.NormalizePhoneOrThrow(contactPhone, nameof(request.ContactPhone));
        CustomBookingRequestSupport.EnsureValidEmailIfProvided(contactEmail, nameof(request.ContactEmail));
        var service = await CustomBookingRequestSupport.ResolveVesselRentalServiceAsync(
            _context,
            request.ServiceId,
            cancellationToken);
        CustomBookingRequestSupport.EnsureDepartureIsInFuture(
            request.DepartureDate,
            request.PreferredStartTime,
            _timeProvider.GetUtcNow());

        var contactUserId = await ResolveContactUserIdAsync(normalizedPhone, contactEmail, cancellationToken);
        var tripPlan = await CustomBookingTripPlanner.BuildAsync(_context, request, cancellationToken);
        var passengerCount = request.AdultCount + request.ChildCount;

        var customRequest = new CustomBookingRequest
        {
            UserId = user.Id,
            ContactUserId = contactUserId,
            ContactName = contactName.Trim(),
            ContactPhone = normalizedPhone,
            ContactEmail = contactEmail.Trim(),
            WaterbusServiceId = service.Id,
            WaterbusService = service,
            RequestedNumberOfDecks = request.RequestedNumberOfDecks,
            RequestedSeatSetupType = request.RequestedSeatSetupType,
            DepartureDate = request.DepartureDate,
            PreferredStartTime = request.PreferredStartTime,
            PreferredEndTime = tripPlan.RouteEstimate.EstimatedEndTime,
            EstimatedEndDate = tripPlan.RouteEstimate.EstimatedEndDate,
            EstimatedTravelMinutes = tripPlan.RouteEstimate.EstimatedTravelMinutes,
            EstimatedStayMinutes = tripPlan.RouteEstimate.EstimatedStayMinutes,
            BufferMinutes = tripPlan.RouteEstimate.BufferMinutes,
            EstimatedDurationMinutes = tripPlan.RouteEstimate.EstimatedDurationMinutes,
            FromLocation = tripPlan.FromStation.StationName,
            ToLocation = tripPlan.ToStation.StationName,
            FromStationId = tripPlan.FromStation.Id,
            FromStationCode = tripPlan.FromStation.StationCode,
            ToStationId = tripPlan.ToStation.Id,
            ToStationCode = tripPlan.ToStation.StationCode,
            ItineraryNote = null,
            PassengerCount = passengerCount,
            AdultCount = request.AdultCount,
            ChildCount = request.ChildCount,
            SpecialRequests = string.IsNullOrWhiteSpace(request.SpecialRequests)
                ? null
                : request.SpecialRequests.Trim(),
            Status = CustomBookingRequestStatus.PendingReview,
            ItineraryStops = tripPlan.ItineraryStops.ToList()
        };

        _context.Set<CustomBookingRequest>().Add(customRequest);
        await _context.SaveChangesAsync(cancellationToken);

        customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleAsync(x => x.Id == customRequest.Id, cancellationToken);

        return CustomBookingRequestDto.From(customRequest, tripPlan.RouteSegments);
    }

    private static string ResolveContactName(CreateCustomBookingRequestCommand request, User user)
    {
        if (request.UseAccountContact && !string.IsNullOrWhiteSpace(user.FullName))
        {
            return user.FullName;
        }

        if (!string.IsNullOrWhiteSpace(request.ContactName))
        {
            return request.ContactName;
        }

        throw AuthSupport.CreateValidationException(nameof(request.ContactName), "Tên người liên hệ là bắt buộc.");
    }

    private static string ResolveContactPhone(CreateCustomBookingRequestCommand request, User user)
    {
        if (request.UseAccountContact && !string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            return user.PhoneNumber;
        }

        if (!string.IsNullOrWhiteSpace(request.ContactPhone))
        {
            return request.ContactPhone;
        }

        throw AuthSupport.CreateValidationException(nameof(request.ContactPhone), "Số điện thoại liên hệ là bắt buộc.");
    }

    internal static string ResolveContactEmail(CreateCustomBookingRequestCommand request, User user)
    {
        if (request.UseAccountContact && !string.IsNullOrWhiteSpace(user.Email))
        {
            return user.Email;
        }

        if (!string.IsNullOrWhiteSpace(request.ContactEmail))
        {
            return request.ContactEmail;
        }

        throw AuthSupport.CreateValidationException(
            nameof(request.ContactEmail),
            request.UseAccountContact
                ? "Tài khoản chưa có email. Vui lòng nhập email nhận thông tin vé cho yêu cầu này."
                : "Email liên hệ là bắt buộc.");
    }

    private async Task<Guid?> ResolveContactUserIdAsync(
        string normalizedPhone,
        string contactEmail,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = contactEmail.Trim().ToUpperInvariant();

        var linkedUser = await _context.Set<User>()
            .Where(x => x.NormalizedPhoneNumber == normalizedPhone
                     || x.NormalizedEmail == normalizedEmail)
            .FirstOrDefaultAsync(cancellationToken);

        return linkedUser?.Id;
    }

}
