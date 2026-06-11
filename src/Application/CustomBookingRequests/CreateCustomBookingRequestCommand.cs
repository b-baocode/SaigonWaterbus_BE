using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record CreateCustomBookingRequestCommand(
    bool UseAccountContact,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    DateOnly DepartureDate,
    TimeOnly? PreferredStartTime,
    TimeOnly? PreferredEndTime,
    string FromLocation,
    string ToLocation,
    string? FromStationCode,
    string? ToStationCode,
    string? ItineraryNote,
    int PassengerCount,
    string? SpecialRequests) : IRequest<CustomBookingRequestDto>;

public sealed class CreateCustomBookingRequestCommandValidator : AbstractValidator<CreateCustomBookingRequestCommand>
{
    public CreateCustomBookingRequestCommandValidator()
    {
        RuleFor(x => x.DepartureDate)
            .Must(x => x != default)
            .WithMessage("Ngày đi là bắt buộc.");

        RuleFor(x => x.FromLocation).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ToLocation).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FromStationCode).MaximumLength(50).When(x => x.FromStationCode is not null);
        RuleFor(x => x.ToStationCode).MaximumLength(50).When(x => x.ToStationCode is not null);
        RuleFor(x => x.ItineraryNote).MaximumLength(1000).When(x => x.ItineraryNote is not null);
        RuleFor(x => x.PassengerCount).GreaterThan(0).LessThanOrEqualTo(500);
        RuleFor(x => x.SpecialRequests).MaximumLength(1000).When(x => x.SpecialRequests is not null);

        When(x => !x.UseAccountContact, () =>
        {
            RuleFor(x => x.ContactName).NotEmpty().MaximumLength(150);
            RuleFor(x => x.ContactPhone).NotEmpty().MaximumLength(20);
        });

        RuleFor(x => x.ContactName).MaximumLength(150).When(x => x.ContactName is not null);
        RuleFor(x => x.ContactPhone).MaximumLength(20).When(x => x.ContactPhone is not null);
        RuleFor(x => x.ContactEmail).MaximumLength(255).When(x => x.ContactEmail is not null);
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
        CustomBookingRequestSupport.EnsureDepartureDateIsNotPast(
            request.DepartureDate,
            CustomBookingRequestSupport.GetVietnamToday(_timeProvider));
        CustomBookingRequestSupport.EnsurePreferredTimeRangeIsValid(
            request.PreferredStartTime,
            request.PreferredEndTime);

        var contactUserId = await ResolveContactUserIdAsync(normalizedPhone, contactEmail, cancellationToken);
        var fromStation = await ResolveStationAsync(request.FromStationCode, nameof(request.FromStationCode), cancellationToken);
        var toStation = await ResolveStationAsync(request.ToStationCode, nameof(request.ToStationCode), cancellationToken);

        var customRequest = new CustomBookingRequest
        {
            UserId = user.Id,
            ContactUserId = contactUserId,
            ContactName = contactName.Trim(),
            ContactPhone = normalizedPhone,
            ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim(),
            DepartureDate = request.DepartureDate,
            PreferredStartTime = request.PreferredStartTime,
            PreferredEndTime = request.PreferredEndTime,
            FromLocation = request.FromLocation.Trim(),
            ToLocation = request.ToLocation.Trim(),
            FromStationId = fromStation?.Id,
            FromStationCode = fromStation?.StationCode,
            ToStationId = toStation?.Id,
            ToStationCode = toStation?.StationCode,
            ItineraryNote = string.IsNullOrWhiteSpace(request.ItineraryNote) ? null : request.ItineraryNote.Trim(),
            PassengerCount = request.PassengerCount,
            SpecialRequests = string.IsNullOrWhiteSpace(request.SpecialRequests) ? null : request.SpecialRequests.Trim(),
            Status = CustomBookingRequestStatus.PendingReview
        };

        _context.Set<CustomBookingRequest>().Add(customRequest);
        await _context.SaveChangesAsync(cancellationToken);

        customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleAsync(x => x.Id == customRequest.Id, cancellationToken);

        return CustomBookingRequestDto.From(customRequest);
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

    private static string? ResolveContactEmail(CreateCustomBookingRequestCommand request, User user)
    {
        if (request.UseAccountContact && !string.IsNullOrWhiteSpace(user.Email))
        {
            return user.Email;
        }

        return string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail;
    }

    private async Task<Guid?> ResolveContactUserIdAsync(
        string normalizedPhone,
        string? contactEmail,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = string.IsNullOrWhiteSpace(contactEmail)
            ? null
            : contactEmail.Trim().ToUpperInvariant();

        var linkedUser = await _context.Set<User>()
            .Where(x => x.NormalizedPhoneNumber == normalizedPhone
                     || (normalizedEmail != null && x.NormalizedEmail == normalizedEmail))
            .FirstOrDefaultAsync(cancellationToken);

        return linkedUser?.Id;
    }

    private async Task<Station?> ResolveStationAsync(
        string? stationCode,
        string propertyName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stationCode))
        {
            return null;
        }

        var normalizedCode = CustomBookingRequestSupport.NormalizeStationCode(stationCode);
        var station = await _context.Set<Station>()
            .SingleOrDefaultAsync(x => x.StationCode == normalizedCode, cancellationToken);

        if (station is null)
        {
            throw AuthSupport.CreateValidationException(propertyName, $"Bến '{normalizedCode}' không tồn tại.");
        }

        return station;
    }
}
